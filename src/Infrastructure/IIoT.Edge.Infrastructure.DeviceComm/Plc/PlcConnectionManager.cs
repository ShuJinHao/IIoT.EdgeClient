using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcConnectionManager : IPlcConnectionManager
{
    private readonly PlcRuntimeRegistry _runtimeRegistry;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly PlcLifecycleCoordinator _lifecycleCoordinator;
    private readonly IProductionContextStore _contextStore;

    public PlcConnectionManager(
        PlcRuntimeRegistry runtimeRegistry,
        PlcConnectionStatusStore statusStore,
        PlcLifecycleCoordinator lifecycleCoordinator,
        IProductionContextStore contextStore)
    {
        _runtimeRegistry = runtimeRegistry;
        _statusStore = statusStore;
        _lifecycleCoordinator = lifecycleCoordinator;
        _contextStore = contextStore;
    }

    public Task InitializeAsync(CancellationToken ct = default)
        => _lifecycleCoordinator.InitializeAsync(ct);

    public Task StopAsync(CancellationToken ct = default)
        => _lifecycleCoordinator.StopAsync(ct);

    public Task ReloadAsync(string deviceName, CancellationToken ct = default)
        => _lifecycleCoordinator.ReloadAsync(deviceName, ct);

    public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default)
        => _lifecycleCoordinator.StopDeviceAsync(networkDeviceId, ct);

    public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        => _runtimeRegistry.RegisterTaskFactory(deviceName, factory);

    public IPlcService? GetPlc(int networkDeviceId)
        => _runtimeRegistry.GetPlc(networkDeviceId);

    public ProductionContext? GetContext(string deviceName)
        => _contextStore.GetOrCreate(deviceName);

    public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
    {
        _runtimeRegistry.BlockRuntime(deviceName);
        _statusStore.MarkRuntimeFault(networkDeviceId, deviceName, error);
    }

    public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
        => _statusStore.GetSnapshot(networkDeviceId);

    public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
        => _statusStore.GetSnapshots();

    public void Dispose()
        => _lifecycleCoordinator.Dispose();

    public ValueTask DisposeAsync()
        => _lifecycleCoordinator.DisposeAsync();
}
