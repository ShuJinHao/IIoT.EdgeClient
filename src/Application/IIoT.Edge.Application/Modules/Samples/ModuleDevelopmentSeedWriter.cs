using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Modules.Samples;

public sealed record ModuleFirstInitializationApplyResult(
    bool AlreadyInitialized,
    bool ExistingDatabaseAdopted,
    ModuleDevelopmentSeedResult SeedResult);

public interface IModuleFirstInitializationStore
{
    Task<ModuleFirstInitializationApplyResult> ApplyAsync(
        ModuleFirstInitializationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 宿主侧正式 ModuleSeed 物化器。插件只提交 DTO，聚合创建、事务和持久化始终归宿主。
/// </summary>
public sealed class ModuleDevelopmentSeedWriter
    : IModuleDevelopmentSeedWriter,
      IModuleFirstInitializationStore
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
        var result = await ApplyWithinUnitOfWorkAsync(
            unitOfWork,
            request,
            cancellationToken).ConfigureAwait(false);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<ModuleFirstInitializationApplyResult> ApplyAsync(
        ModuleFirstInitializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModuleId);
        ArgumentNullException.ThrowIfNull(request.Descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Descriptor.InitializationMarkerKey);
        if (request.Descriptor.SchemaVersion <= 0 || request.Descriptor.SeedVersion <= 0)
        {
            throw new InvalidOperationException("MODULE_FIRST_INITIALIZATION_VERSION_INVALID");
        }

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);
        var configs = unitOfWork.Repository<SystemConfigEntity>();
        var markerKey = request.Descriptor.InitializationMarkerKey.Trim();
        var existingMarker = await configs.GetAsync(
            item => item.Key == markerKey,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existingMarker is not null)
        {
            return new ModuleFirstInitializationApplyResult(
                AlreadyInitialized: true,
                ExistingDatabaseAdopted: false,
                EmptySeedResult());
        }

        var existingDatabaseAdopted = request.Descriptor.RunOnlyForNewDatabase
            && await HasExistingPluginDataAsync(unitOfWork, cancellationToken).ConfigureAwait(false);
        var seedResult = existingDatabaseAdopted
            ? EmptySeedResult()
            : await ApplyWithinUnitOfWorkAsync(
                unitOfWork,
                new ModuleDevelopmentSeedRequest(
                    request.ModuleId,
                    ResetBeforeImport: false,
                    Devices: request.Devices),
                cancellationToken).ConfigureAwait(false);

        configs.Add(SystemConfigEntity.Create(
            markerKey,
            $"schema={request.Descriptor.SchemaVersion};seed={request.Descriptor.SeedVersion};" +
            $"client={request.ClientCode.Trim()};module={request.ModuleId.Trim()};" +
            $"completed={DateTimeOffset.UtcNow:O}",
            existingDatabaseAdopted
                ? "Existing plugin database adopted without replaying initial seed."
                : "Plugin first initialization completed atomically."));
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ModuleFirstInitializationApplyResult(
            AlreadyInitialized: false,
            existingDatabaseAdopted,
            seedResult);
    }

    private static async Task<bool> HasExistingPluginDataAsync(
        IEdgeUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if ((await unitOfWork.Repository<NetworkDeviceEntity>()
                .GetListAsync(static _ => true, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return true;
        }

        if ((await unitOfWork.Repository<IoMappingEntity>()
                .GetListAsync(static _ => true, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return true;
        }

        if ((await unitOfWork.Repository<PlcTaskBindingEntity>()
                .GetListAsync(static _ => true, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return true;
        }

        // 宿主或配置迁移可能在首次插件启动前先写入 SystemConfig。
        // 任意通用配置不是“插件已播种”的证据，否则新库会永久跳过 PLC 初始化。
        return false;
    }

    private static ModuleDevelopmentSeedResult EmptySeedResult()
        => new(0, 0, 0, 0) { ImportedTaskBindingCount = 0 };

    private static async Task<ModuleDevelopmentSeedResult> ApplyWithinUnitOfWorkAsync(
        IEdgeUnitOfWork unitOfWork,
        ModuleDevelopmentSeedRequest request,
        CancellationToken cancellationToken)
    {
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
