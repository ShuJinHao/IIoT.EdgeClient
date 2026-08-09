using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Hardware;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal sealed class MonitorConfiguredDeviceLoader(
    IStationRuntimeRegistry runtimeRegistry,
    IPlcTaskBindingService taskBindingService,
    ISender sender) : IMonitorConfiguredDeviceLoader
{
    public async Task<IReadOnlyList<DevicePluginPlcSnapshot>> LoadConfiguredPlcDevicesAsync(CancellationToken ct)
    {
        var devicesResult = await sender.Send(new GetAllNetworkDevicesQuery(), ct).ConfigureAwait(false);
        if (!devicesResult.IsSuccess || devicesResult.Value is null)
        {
            return [];
        }

        return devicesResult.Value
            .Where(static device =>
                device.DeviceType == DeviceType.PLC
                && !string.IsNullOrWhiteSpace(device.DeviceName))
            .OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, PlcTaskBindingDeviceDto>> LoadTaskBindingsByDeviceAsync(
        IReadOnlyCollection<DevicePluginPlcSnapshot> configuredPlcs,
        CancellationToken ct)
    {
        var result = new Dictionary<int, PlcTaskBindingDeviceDto>();
        var moduleIds = runtimeRegistry.GetRegistrations().Keys.ToArray();
        if (moduleIds.Length != 1 || !HasRuntimeFactory(moduleIds[0]))
        {
            return result;
        }

        var moduleBindings = await taskBindingService
            .GetModuleDeviceBindingsFromMemoryAsync(moduleIds[0], ct)
            .ConfigureAwait(false);
        foreach (var deviceBinding in moduleBindings)
        {
            result[deviceBinding.NetworkDeviceId] = deviceBinding;
        }

        return result;
    }

    public bool HasRuntimeFactory(string? moduleId)
        => !string.IsNullOrWhiteSpace(moduleId)
            && runtimeRegistry.TryGetFactory(moduleId, out _);
}
