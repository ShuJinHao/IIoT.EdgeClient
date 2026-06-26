using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed class IoMappingSchemaSource(
    IRepository<NetworkDeviceEntity> networkDevices,
    ModuleHardwareProfileResolver hardwareProfileResolver) : IConfigSchemaSource
{
    public string SchemaId => IoMappingSchemaIds.Signals;

    public async Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = hardwareProfileResolver.Resolve();
        if (profile is null)
        {
            return [];
        }

        var devices = await networkDevices
            .GetListAsync(static x => x.DeviceType == DeviceType.PLC, cancellationToken)
            .ConfigureAwait(false);
        var items = new List<ConfigSchemaItem>();
        foreach (var device in devices.OrderBy(static x => x.Id))
        {
            if (device.Id <= 0)
            {
                continue;
            }

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

        return items
            .GroupBy(static x => x.Key, StringComparer.Ordinal)
            .Select(static x => x.First())
            .ToArray();
    }
}
