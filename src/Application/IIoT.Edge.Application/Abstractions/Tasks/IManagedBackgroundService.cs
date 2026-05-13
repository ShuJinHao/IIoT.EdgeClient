namespace IIoT.Edge.Application.Abstractions.Tasks;

public interface IManagedBackgroundService
{
    string ServiceName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
