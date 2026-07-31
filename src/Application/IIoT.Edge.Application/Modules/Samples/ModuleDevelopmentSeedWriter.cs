using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Modules.Samples;

/// <summary>
/// 宿主侧正式 ModuleSeed 物化器。插件只提交 DTO，聚合创建、事务和持久化始终归宿主。
/// </summary>
public sealed class ModuleDevelopmentSeedWriter : IModuleDevelopmentSeedWriter
{
    private readonly IEdgeUnitOfWorkFactory _unitOfWorkFactory;

    public ModuleDevelopmentSeedWriter(IEdgeUnitOfWorkFactory unitOfWorkFactory)
        => _unitOfWorkFactory = unitOfWorkFactory;

    public async Task<ModuleDevelopmentSeedResult> ApplyAsync(
        ModuleDevelopmentSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModuleId);
        if (request.ResetBeforeImport)
        {
            throw new InvalidOperationException(
                "MODULE_SEED_RESET_FORBIDDEN：正式播种只允许补缺失项，禁止删除或重置现场配置。");
        }

        ValidateStableIdentities(request);

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);
        var devices = unitOfWork.Repository<NetworkDeviceEntity>();
        var mappings = unitOfWork.Repository<IoMappingEntity>();
        var taskBindings = unitOfWork.Repository<PlcTaskBindingEntity>();

        var importedDeviceCount = 0;
        var importedMappingCount = 0;
        var importedTaskBindingCount = 0;
        foreach (var seed in request.Devices)
        {
            var device = await FindExistingDeviceAsync(devices, seed, cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                device = CreateDevice(seed);
                devices.Add(device);
                // 仅为生成 identity；事务尚未提交，后续任一失败仍整体回滚。
                await unitOfWork.FlushAsync(cancellationToken).ConfigureAwait(false);
                importedDeviceCount++;
            }

            var existingMappings = await mappings.GetListAsync(
                mapping => mapping.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var existingMappingKeys = existingMappings
                .Select(static mapping => BuildMappingIdentity(mapping.SignalKey, mapping.Direction))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var templates = seed.IoMappings
                .Where(static template => !string.IsNullOrWhiteSpace(template.PlcAddress))
                .Where(template => !existingMappingKeys.Contains(
                    BuildMappingIdentity(template.SignalKey, template.Direction)))
                .DistinctBy(
                    template => BuildMappingIdentity(template.SignalKey, template.Direction),
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(static template => template.SortOrder)
                .ThenBy(static template => template.Direction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var template in templates)
            {
                mappings.Add(CreateMapping(device.Id, template));
            }

            importedMappingCount += templates.Length;

            var existingBindings = await taskBindings.GetListAsync(
                binding => binding.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var existingTaskKeys = existingBindings
                .Select(static binding => binding.TaskKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingTaskBindings = seed.TaskBindings
                .Where(static binding => !string.IsNullOrWhiteSpace(binding.TaskKey))
                .Where(binding => !existingTaskKeys.Contains(binding.TaskKey.Trim()))
                .DistinctBy(static binding => binding.TaskKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var updatedAt = DateTimeOffset.UtcNow;
            foreach (var binding in missingTaskBindings)
            {
                taskBindings.Add(PlcTaskBindingEntity.Create(
                    device.Id,
                    binding.TaskKey,
                    binding.Enabled,
                    updatedAt));
            }

            importedTaskBindingCount += missingTaskBindings.Length;
        }

        // 本请求唯一 durable commit；FlushAsync 只在同一事务中物化新设备 identity。
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ModuleDevelopmentSeedResult(
            importedDeviceCount,
            importedMappingCount,
            0,
            0)
        {
            ImportedTaskBindingCount = importedTaskBindingCount
        };
    }

    private static async Task<NetworkDeviceEntity?> FindExistingDeviceAsync(
        IRepository<NetworkDeviceEntity> devices,
        ModuleDevelopmentDeviceSeed seed,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(seed.PlcCode))
        {
            var plcCode = seed.PlcCode.Trim();
            var byPlcCode = await devices.GetAsync(
                candidate => candidate.PlcCode == plcCode,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (byPlcCode is not null)
            {
                return byPlcCode;
            }

            var nameConflict = await devices.GetAsync(
                candidate => candidate.DeviceName == seed.DeviceName,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (nameConflict is not null)
            {
                throw new InvalidOperationException(
                    "MODULE_SEED_DEVICE_NAME_CONFLICT：设备名称已属于其它 PlcCode，禁止按名称认领或覆盖。");
            }

            // 正式 PlcCode 播种不允许按可变 DeviceName 认领现场设备。
            return null;
        }

        // Host API 2.0.x 的无 PlcCode 旧调用保留二进制兼容，仅此路径允许名称回退。
        return await devices.GetAsync(
            candidate => candidate.DeviceName == seed.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateStableIdentities(ModuleDevelopmentSeedRequest request)
    {
        var plcCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in request.Devices)
        {
            if (string.IsNullOrWhiteSpace(seed.PlcCode))
            {
                continue;
            }

            if (!plcCodes.Add(seed.PlcCode.Trim()))
            {
                throw new InvalidOperationException(
                    $"MODULE_SEED_PLC_CODE_DUPLICATE：模块“{request.ModuleId}”重复声明 PlcCode。");
            }
        }
    }

    private static string BuildMappingIdentity(string signalKey, string direction)
        => $"{signalKey.Trim()}\u001f{direction.Trim()}";

    private static NetworkDeviceEntity CreateDevice(ModuleDevelopmentDeviceSeed seed)
    {
        var device = NetworkDeviceEntity.Create(
            seed.DeviceName,
            DeviceType.PLC,
            seed.IpAddress,
            seed.Port,
            seed.PlcCode);
        device.UpdateDeviceModel(seed.DeviceModel);
        device.UpdateProtocolFrame(seed.ProtocolFrame);
        device.UpdateEndpoint(device.IpAddress, device.Port1, device.Port2, seed.ConnectTimeout);
        device.SetEnabled(seed.IsEnabled);
        device.UpdateRemark(seed.Remark);
        return device;
    }

    private static IoMappingEntity CreateMapping(
        int networkDeviceId,
        ModuleIoTemplateEntry template)
    {
        var mapping = IoMappingEntity.Create(
            networkDeviceId,
            template.SignalKey,
            template.PlcAddress,
            template.AddressCount,
            template.DataType,
            template.Direction,
            template.Category,
            template.BusinessGroup);
        mapping.UpdateSortOrder(template.SortOrder);
        mapping.UpdateMetadata(
            template.SignalKey,
            template.DataType,
            template.Direction,
            template.Category,
            template.BusinessGroup,
            template.Remark);
        return mapping;
    }
}
