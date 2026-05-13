namespace IIoT.Edge.Application.Abstractions.Tasks;

public interface IBackgroundServiceCoordinator
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
