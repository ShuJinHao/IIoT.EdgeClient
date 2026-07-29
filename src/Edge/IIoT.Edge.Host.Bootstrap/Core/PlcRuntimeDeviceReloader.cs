using IIoT.Edge.Infrastructure.DeviceComm.Plc;

namespace IIoT.Edge.Shell.Core;

public interface IPlcRuntimeDeviceReloader
{
    Task ReloadDeviceAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default);
}

public sealed class PlcRuntimeDeviceReloader(
    PlcLifecycleCoordinator lifecycleCoordinator) : IPlcRuntimeDeviceReloader
{
    public Task ReloadDeviceAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default)
        => lifecycleCoordinator.ReloadDeviceAsync(networkDeviceId, cancellationToken);
}
