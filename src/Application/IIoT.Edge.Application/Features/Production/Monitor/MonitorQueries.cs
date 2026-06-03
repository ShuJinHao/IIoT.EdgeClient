using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Modules;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.Monitor;

public record GetMonitorSnapshotQuery : IRequest<List<DeviceMonitorSnapshot>>;

public class GetMonitorSnapshotHandler : IRequestHandler<GetMonitorSnapshotQuery, List<DeviceMonitorSnapshot>>
{
    private readonly IProductionContextStore _contextStore;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly IMonitorConfiguredDeviceLoader _configuredDeviceLoader;
    private readonly IMonitorSnapshotSourceMatcher _sourceMatcher;
    private readonly IMonitorSnapshotProjectionBuilder _projectionBuilder;

    public GetMonitorSnapshotHandler(
        IProductionContextStore contextStore,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IMonitorConfiguredDeviceLoader configuredDeviceLoader,
        IMonitorSnapshotSourceMatcher sourceMatcher,
        IMonitorSnapshotProjectionBuilder projectionBuilder)
    {
        _contextStore = contextStore;
        _diagnosticsQuery = diagnosticsQuery;
        _configuredDeviceLoader = configuredDeviceLoader;
        _sourceMatcher = sourceMatcher;
        _projectionBuilder = projectionBuilder;
    }

    public async Task<List<DeviceMonitorSnapshot>> Handle(GetMonitorSnapshotQuery request, CancellationToken ct)
    {
        var diagnostics = await _diagnosticsQuery.GetCurrentAsync(ct).ConfigureAwait(false);
        var result = new List<DeviceMonitorSnapshot>();
        var contexts = _contextStore.GetAll().ToList();
        var runtimeStatuses = _sourceMatcher.GetRuntimeStatuses();
        var configuredPlcs = await _configuredDeviceLoader.LoadConfiguredPlcDevicesAsync(ct).ConfigureAwait(false);
        var taskBindingsByDevice = await _configuredDeviceLoader
            .LoadTaskBindingsByDeviceAsync(configuredPlcs, ct)
            .ConfigureAwait(false);

        foreach (var context in contexts)
        {
            var runtimeStatus = _sourceMatcher.ResolveRuntimeStatus(context);
            var configuredDevice = _sourceMatcher.ResolveConfiguredDevice(context, configuredPlcs);
            result.Add(_projectionBuilder.BuildContextSnapshot(
                context,
                runtimeStatus,
                configuredDevice,
                diagnostics,
                taskBindingsByDevice));
        }

        foreach (var runtimeStatus in runtimeStatuses
            .Where(runtimeStatus => !MonitorDeviceIdentityHelper.HasContextForRuntimeStatus(contexts, runtimeStatus))
            .GroupBy(MonitorDeviceIdentityHelper.RuntimeStatusKey)
            .Select(static group => group.First()))
        {
            var configuredDevice = _sourceMatcher.ResolveConfiguredDevice(runtimeStatus, configuredPlcs);
            result.Add(_projectionBuilder.BuildRuntimeOnlySnapshot(
                runtimeStatus,
                configuredDevice,
                diagnostics,
                taskBindingsByDevice));
        }

        foreach (var device in configuredPlcs
            .Where(device => !MonitorDeviceIdentityHelper.HasMonitorSourceForConfiguredDevice(contexts, runtimeStatuses, device))
            .GroupBy(MonitorDeviceIdentityHelper.ConfiguredDeviceKey)
            .Select(static group => group.First()))
        {
            result.Add(_projectionBuilder.BuildConfiguredDeviceSnapshot(device, diagnostics, taskBindingsByDevice));
        }

        return result;
    }
}
