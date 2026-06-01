using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal sealed class MonitorConfiguredDeviceLoader(
    IStationRuntimeRegistry runtimeRegistry,
    IPlcTaskBindingService taskBindingService,
    ISender sender) : IMonitorConfiguredDeviceLoader
{
    public async Task<IReadOnlyList<NetworkDeviceEntity>> LoadConfiguredPlcDevicesAsync(CancellationToken ct)
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
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
        CancellationToken ct)
    {
        var result = new Dictionary<int, PlcTaskBindingDeviceDto>();
        var moduleIds = configuredPlcs
            .Select(static device => device.ModuleId)
            .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var moduleId in moduleIds)
        {
            if (!HasRuntimeFactory(moduleId))
            {
                continue;
            }

            var moduleBindings = await taskBindingService
                .GetModuleDeviceBindingsAsync(moduleId, ct)
                .ConfigureAwait(false);
            foreach (var deviceBinding in moduleBindings)
            {
                result[deviceBinding.NetworkDeviceId] = deviceBinding;
            }
        }

        return result;
    }

    public bool HasRuntimeFactory(string? moduleId)
        => !string.IsNullOrWhiteSpace(moduleId)
            && runtimeRegistry.TryGetFactory(moduleId, out _);
}
