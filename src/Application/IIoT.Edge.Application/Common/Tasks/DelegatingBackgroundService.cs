using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class DelegatingBackgroundService : IManagedBackgroundService
{
    private readonly Func<CancellationToken, Task> _startAsync;
    private readonly Func<CancellationToken, Task> _stopAsync;

    public DelegatingBackgroundService(
        string serviceName,
        Func<CancellationToken, Task> startAsync,
        Func<CancellationToken, Task>? stopAsync = null)
    {
        ServiceName = serviceName;
        _startAsync = startAsync ?? throw new ArgumentNullException(nameof(startAsync));
        _stopAsync = stopAsync ?? (_ => Task.CompletedTask);
    }

    public string ServiceName { get; }

    public Task StartAsync(CancellationToken cancellationToken)
        => _startAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => _stopAsync(cancellationToken);
}
