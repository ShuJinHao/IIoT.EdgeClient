namespace IIoT.Edge.AvaloniaShell.Services;

public interface IAvaloniaShellStartupCoordinator
{
    bool ShouldStartRuntime(IEnumerable<string>? arguments);

    Task<AvaloniaShellStartupResult> StartAsync(
        IEnumerable<string>? arguments,
        CancellationToken cancellationToken = default);

    Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
