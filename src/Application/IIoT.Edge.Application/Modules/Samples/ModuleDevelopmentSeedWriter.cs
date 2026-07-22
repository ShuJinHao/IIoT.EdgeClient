using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Modules.Samples;

/// <summary>
/// 宿主侧开发模板物化器。插件只提交 DTO，聚合创建、事务和持久化始终归宿主。
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

        await using var unitOfWork = await _unitOfWorkFactory.BeginAsync(cancellationToken).ConfigureAwait(false);
        var devices = unitOfWork.Repository<NetworkDeviceEntity>();
        var mappings = unitOfWork.Repository<IoMappingEntity>();

        var resetDeviceCount = 0;
        var resetMappingCount = 0;
        if (request.ResetBeforeImport)
        {
            var existingDevices = await devices.GetListAsync(_ => true, cancellationToken).ConfigureAwait(false);
            var deviceIds = existingDevices.Select(static device => device.Id).ToHashSet();
            var existingMappings = deviceIds.Count == 0
                ? []
                : await mappings.GetListAsync(
                    mapping => deviceIds.Contains(mapping.NetworkDeviceId),
                    cancellationToken).ConfigureAwait(false);

            foreach (var mapping in existingMappings)
            {
                mappings.Delete(mapping);
            }

            foreach (var device in existingDevices)
            {
                devices.Delete(device);
            }

            resetDeviceCount = existingDevices.Count;
            resetMappingCount = existingMappings.Count;
            if (resetDeviceCount > 0 || resetMappingCount > 0)
            {
                // 仍处于同一事务；先物化删除，避免同 PlcCode 重建时撞唯一约束。
                await unitOfWork.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var importedDeviceCount = 0;
        var importedMappingCount = 0;
        foreach (var seed in request.Devices)
        {
            var device = await devices.GetAsync(
                candidate => candidate.DeviceName == seed.DeviceName,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
            if (existingMappings.Count > 0)
            {
                continue;
            }

            var templates = seed.IoMappings
                .Where(static template => !string.IsNullOrWhiteSpace(template.PlcAddress))
                .OrderBy(static template => template.SortOrder)
                .ThenBy(static template => template.Direction, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static template => template.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var template in templates)
            {
                mappings.Add(CreateMapping(device.Id, template));
            }

            importedMappingCount += templates.Length;
        }

        // 本请求唯一 durable commit；FlushAsync 只在同一事务中物化删除/identity。
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ModuleDevelopmentSeedResult(
            importedDeviceCount,
            importedMappingCount,
            resetDeviceCount,
            resetMappingCount);
    }

    private static NetworkDeviceEntity CreateDevice(ModuleDevelopmentDeviceSeed seed)
    {
        var device = NetworkDeviceEntity.Create(
            seed.DeviceName,
            DeviceType.PLC,
            seed.IpAddress,
            seed.Port);
        device.UpdateDeviceModel(seed.DeviceModel);
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
