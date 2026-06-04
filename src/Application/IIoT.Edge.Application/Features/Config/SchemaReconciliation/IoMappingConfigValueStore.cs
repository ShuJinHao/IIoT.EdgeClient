using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class IoMappingConfigValueStore(
    IRepository<NetworkDeviceEntity> networkDevices,
    IRepository<IoMappingEntity> ioMappings,
    ModuleHardwareProfileResolver hardwareProfileResolver) : IConfigValueStore
{
    public string SchemaId => IoMappingSchemaIds.Signals;

    public async Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var managedDeviceIds = await LoadManagedDeviceIdsAsync(cancellationToken).ConfigureAwait(false);
        if (managedDeviceIds.Count == 0)
        {
            return [];
        }

        var deviceIds = managedDeviceIds.ToArray();
        var mappings = await ioMappings
            .GetListAsync(x => deviceIds.Contains(x.NetworkDeviceId), cancellationToken)
            .ConfigureAwait(false);

        return mappings
            .Where(static x => !string.IsNullOrWhiteSpace(x.SignalKey)
                               && !string.IsNullOrWhiteSpace(x.Direction))
            .Select(static x => IoMappingSchemaKey.Create(x.NetworkDeviceId, x.Direction, x.SignalKey))
            .ToArray();
    }

    public async Task InsertAsync(
        ConfigSchemaItem item,
        CancellationToken cancellationToken = default)
    {
        var networkDeviceId = IoMappingSchemaMetadata.GetNetworkDeviceId(item);
        var signalKey = IoMappingSchemaMetadata.GetSignalKey(item);
        var addressCount = IoMappingSchemaMetadata.GetAddressCount(item);
        var dataType = IoMappingSchemaMetadata.GetDataType(item);
        var direction = IoMappingSchemaMetadata.GetDirection(item);
        var category = IoMappingSchemaMetadata.GetCategory(item);
        var businessGroup = IoMappingSchemaMetadata.GetBusinessGroup(item);
        var sortOrder = IoMappingSchemaMetadata.GetSortOrder(item);
        var remark = IoMappingSchemaMetadata.GetRemark(item);

        var entity = IoMappingEntity.Create(
            networkDeviceId,
            signalKey,
            item.DefaultValue,
            addressCount,
            dataType,
            direction,
            category,
            businessGroup);
        entity.UpdateSortOrder(sortOrder);
        entity.UpdateMetadata(signalKey, dataType, direction, category, businessGroup, remark);
        ioMappings.Add(entity);
        await ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IoMappingSchemaKey.TryParse(key, out var parsed))
        {
            return;
        }

        var candidates = await ioMappings
            .GetListAsync(x => x.NetworkDeviceId == parsed.NetworkDeviceId, cancellationToken)
            .ConfigureAwait(false);
        var matches = candidates
            .Where(x => parsed.Matches(x.Direction, x.SignalKey))
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        foreach (var mapping in matches)
        {
            ioMappings.Delete(mapping);
        }

        await ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyCollection<int>> LoadManagedDeviceIdsAsync(CancellationToken cancellationToken)
    {
        if (hardwareProfileResolver.Resolve() is null)
        {
            return [];
        }

        var devices = await networkDevices
            .GetListAsync(static x => x.DeviceType == DeviceType.PLC, cancellationToken)
            .ConfigureAwait(false);

        return devices
            .Where(static x => x.Id > 0)
            .Select(static x => x.Id)
            .Distinct()
            .ToArray();
    }
}
