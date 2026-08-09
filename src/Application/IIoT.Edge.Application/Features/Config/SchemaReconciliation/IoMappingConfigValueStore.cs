using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Sdk.Hardware;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class IoMappingConfigValueStore(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores,
    ModuleHardwareProfileResolver hardwareProfileResolver) : IConfigValueStore, IRepairableConfigValueStore
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();

    public string SchemaId => IoMappingSchemaIds.Signals;

    public Task<IReadOnlyCollection<string>> GetExistingKeysAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (hardwareProfileResolver.Resolve() is null || !snapshots.IsInitialized)
        {
            return Task.FromResult<IReadOnlyCollection<string>>([]);
        }

        IReadOnlyCollection<string> keys = snapshots.GetIoPoints()
            .Where(static item => !string.IsNullOrWhiteSpace(item.SignalKey)
                                  && !string.IsNullOrWhiteSpace(item.Direction))
            .Select(static item => IoMappingSchemaKey.Create(
                item.NetworkDeviceId,
                item.Direction,
                item.SignalKey))
            .ToArray();
        return Task.FromResult(keys);
    }

    public async Task InsertAsync(
        ConfigSchemaItem item,
        CancellationToken cancellationToken = default)
    {
        var networkDeviceId = IoMappingSchemaMetadata.GetNetworkDeviceId(item);
        var plc = snapshots.GetPlcs().SingleOrDefault(candidate => candidate.Id == networkDeviceId)
                  ?? throw new InvalidOperationException("PLUGIN_PLC_NOT_FOUND");
        var configuration = new DevicePluginIoPointConfiguration(
            plc.PlcCode,
            IoMappingSchemaMetadata.GetSignalKey(item),
            item.DefaultValue,
            IoMappingSchemaMetadata.GetAddressCount(item),
            IoMappingSchemaMetadata.GetDataType(item),
            IoMappingSchemaMetadata.GetDirection(item),
            IoMappingSchemaMetadata.GetCategory(item),
            IoMappingSchemaMetadata.GetBusinessGroup(item),
            IoMappingSchemaMetadata.GetSortOrder(item),
            IoMappingSchemaMetadata.GetRemark(item));
        await WriteAsync(
            (store, version, token) => store.UpsertIoPointAsync(configuration, version, token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IoMappingSchemaKey.TryParse(key, out var parsed))
        {
            return;
        }

        var plc = snapshots.GetPlcs().SingleOrDefault(candidate => candidate.Id == parsed.NetworkDeviceId);
        if (plc is null)
        {
            return;
        }

        var matches = snapshots.GetIoPoints()
            .Where(item => item.NetworkDeviceId == parsed.NetworkDeviceId
                           && parsed.Matches(item.Direction, item.SignalKey))
            .ToArray();
        foreach (var match in matches)
        {
            await WriteAsync(
                (store, version, token) => store.DeleteIoPointAsync(
                    plc.PlcCode,
                    match.SignalKey,
                    version,
                    token),
                cancellationToken).ConfigureAwait(false);
        }
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

        var matches = snapshots.GetIoPoints()
            .Where(mapping => mapping.NetworkDeviceId == parsed.NetworkDeviceId
                              && parsed.Matches(mapping.Direction, mapping.SignalKey)
                              && mapping.Remark is not null
                              && legacyRemarks.Contains(mapping.Remark, StringComparer.Ordinal))
            .ToArray();
        foreach (var match in matches)
        {
            await WriteAsync(
                (store, version, token) => store.UpsertIoPointAsync(
                    match.Configuration with { Remark = desiredRemark },
                    version,
                    token),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(
        Func<IDevicePluginConfigurationStoreV1, long, CancellationToken,
            Task<DevicePluginConfigurationWriteResult>> write,
        CancellationToken cancellationToken)
    {
        if (_stores.Length != 1)
        {
            throw new InvalidOperationException("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var version = snapshots.GetRequiredSnapshot().ConfigurationVersion;
        var result = await write(_stores[0], version, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                result.FailureReasonCode ?? "PLUGIN_CONFIGURATION_WRITE_REJECTED");
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
