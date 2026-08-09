using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.Hardware;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class IoMappingSchemaSource(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    ModuleHardwareProfileResolver hardwareProfileResolver) : IConfigSchemaSource
{
    public string SchemaId => IoMappingSchemaIds.Signals;

    public Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = hardwareProfileResolver.Resolve();
        if (profile is null || !snapshots.IsInitialized)
        {
            return Task.FromResult<IReadOnlyCollection<ConfigSchemaItem>>([]);
        }

        var items = new List<ConfigSchemaItem>();
        foreach (var device in snapshots.GetPlcs().OrderBy(static item => item.Id))
        {
            foreach (var template in profile.GetIoMappingCandidates())
            {
                var deviceTemplate = profile.ResolveIoTemplateForDevice(device.DeviceName, template);
                if (string.IsNullOrWhiteSpace(deviceTemplate.SignalKey)
                    || string.IsNullOrWhiteSpace(deviceTemplate.Direction))
                {
                    continue;
                }

                items.Add(new ConfigSchemaItem(
                    IoMappingSchemaKey.Create(device.Id, deviceTemplate.Direction, deviceTemplate.SignalKey),
                    deviceTemplate.PlcAddress?.Trim() ?? string.Empty,
                    IoMappingSchemaMetadata.Create(device.Id, deviceTemplate)));
            }
        }

        IReadOnlyCollection<ConfigSchemaItem> result = items
            .GroupBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return Task.FromResult(result);
    }
}
