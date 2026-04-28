using IIoT.Edge.Application.Abstractions.Tasks;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class LongRunningBackgroundTaskService : IManagedBackgroundService
{
    private static readonly TimeSpan StartupFaultObservationWindow = TimeSpan.FromMilliseconds(10);

    private readonly IBackgroundTask _task;
    private CancellationTokenSource? _linkedCts;
    private Task? _executionTask;

    public LongRunningBackgroundTaskService(IBackgroundTask task)
    {
        _task = task;
    }

    public string ServiceName => _task.TaskName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_executionTask is not null)
        {
            return;
        }

        _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionTask = Task.Run(() => _task.StartAsync(_linkedCts.Token), CancellationToken.None);

        // 只观察启动瞬间失败，不等待长跑循环结束。
        var startupProbe = Task.Delay(StartupFaultObservationWindow, cancellationToken);
        var completedTask = await Task.WhenAny(_executionTask, startupProbe).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask == _executionTask)
        {
            await _executionTask.ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_executionTask is null || _linkedCts is null)
        {
            return;
        }

        await _linkedCts.CancelAsync();

        try
        {
            await _executionTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _linkedCts.Dispose();
            _linkedCts = null;
            _executionTask = null;
        }
    }
}
