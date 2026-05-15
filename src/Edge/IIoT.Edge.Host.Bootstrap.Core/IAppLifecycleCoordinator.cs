namespace IIoT.Edge.Host.Bootstrap.Core;

public interface IAppLifecycleCoordinator
{
    Task<AppStartupResult> StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
