using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class IoMappingConfigValueStore(
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IReadRepository<IoMappingEntity> ioMappings,
    IEdgeUnitOfWorkFactory unitOfWorkFactory,
    ModuleHardwareProfileResolver hardwareProfileResolver) : IConfigValueStore, IRepairableConfigValueStore
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
        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        unitOfWork.Repository<IoMappingEntity>().Add(entity);
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IoMappingSchemaKey.TryParse(key, out var parsed))
        {
            return;
        }

        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var repository = unitOfWork.Repository<IoMappingEntity>();
        var candidates = await repository
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
            repository.Delete(mapping);
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RepairExistingAsync(
        ConfigSchemaItem item,
        CancellationToken cancellationToken = default)
    {
        if (!IoMappingSchemaKey.TryParse(item.Key, out var parsed))
        {
            return;
        }

        var desiredRemark = IoMappingSchemaMetadata.GetRemark(item);
        var legacyRemarks = IoMappingSchemaMetadata.GetLegacyRemarks(item);
        if (legacyRemarks.Count == 0)
        {
            return;
        }

        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var repository = unitOfWork.Repository<IoMappingEntity>();
        var candidates = await repository
            .GetListAsync(x => x.NetworkDeviceId == parsed.NetworkDeviceId, cancellationToken)
            .ConfigureAwait(false);
        var changed = false;
        foreach (var mapping in candidates.Where(x => parsed.Matches(x.Direction, x.SignalKey)))
        {
            if (mapping.Remark is null
                || !legacyRemarks.Contains(mapping.Remark, StringComparer.Ordinal))
            {
                continue;
            }

            mapping.UpdateMetadata(
                mapping.SignalKey,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.BusinessGroup,
                desiredRemark);
            repository.Update(mapping);
            changed = true;
        }

        if (changed)
        {
            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
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
