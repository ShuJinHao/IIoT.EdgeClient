using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.AvaloniaShell.Services;

public sealed class AvaloniaShellStartupCoordinator : IAvaloniaShellStartupCoordinator
{
    public const string StartRuntimeArgument = "--start-runtime";

    private readonly IAppLifecycleCoordinator _lifecycleCoordinator;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private bool _runtimeStarted;

    public AvaloniaShellStartupCoordinator(
        IAppLifecycleCoordinator lifecycleCoordinator,
        IAvaloniaRuntimeState runtimeState)
    {
        _lifecycleCoordinator = lifecycleCoordinator ?? throw new ArgumentNullException(nameof(lifecycleCoordinator));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
    }

    public bool ShouldStartRuntime(IEnumerable<string>? arguments)
        => arguments?.Any(argument => string.Equals(argument, StartRuntimeArgument, StringComparison.OrdinalIgnoreCase)) == true;

    public async Task<AvaloniaShellStartupResult> StartAsync(
        IEnumerable<string>? arguments,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldStartRuntime(arguments))
        {
            _runtimeState.SetRuntimeStarted(false);
            return AvaloniaShellStartupResult.UiOnly();
        }

        try
        {
            var startupResult = await _lifecycleCoordinator.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!startupResult.Success)
            {
                return AvaloniaShellStartupResult.Failure(startupResult.Message ?? "AvaloniaShell 启动失败。");
            }

            _runtimeStarted = true;
            _runtimeState.SetRuntimeStarted(true);
            return AvaloniaShellStartupResult.RuntimeStartedOk();
        }
        catch (Exception ex)
        {
            _runtimeState.SetRuntimeStarted(false);
            return AvaloniaShellStartupResult.Failure($"AvaloniaShell 启动失败：{ex.Message}");
        }
    }

    public async Task<bool> StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_runtimeStarted)
        {
            _runtimeState.SetRuntimeStarted(false);
            return true;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _lifecycleCoordinator.StopAsync(timeoutCts.Token).ConfigureAwait(false);
            _runtimeStarted = false;
            _runtimeState.SetRuntimeStarted(false);
            return true;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
