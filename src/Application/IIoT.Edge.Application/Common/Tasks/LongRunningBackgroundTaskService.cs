using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Module.Contracts.Logging;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class LongRunningBackgroundTaskService : IManagedBackgroundService
{
    private readonly IStartupAwareBackgroundTask _task;
    private readonly ILogService? _logger;
    private readonly object _lifecycleSync = new();
    private CancellationTokenSource? _linkedCts;
    private Task? _executionTask;

    public LongRunningBackgroundTaskService(IBackgroundTask task, ILogService? logger = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        _task = task as IStartupAwareBackgroundTask
            ?? throw new ArgumentException(
                $"长运行后台任务 {task.TaskName} 必须实现 {nameof(IStartupAwareBackgroundTask)} 显式启动握手。",
                nameof(task));
        _logger = logger;
    }

    public string ServiceName => _task.TaskName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource linkedCts;
        Task executionTask;
        Task startupTask;
        CancellationToken executionCancellationToken;
        lock (_lifecycleSync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_executionTask is not null)
            {
                if (!_executionTask.IsCompleted)
                {
                    return;
                }

                _executionTask = null;
                _linkedCts?.Dispose();
                _linkedCts = null;
            }

            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            executionCancellationToken = linkedCts.Token;
            try
            {
                var run = _task.StartWithStartup(executionCancellationToken);
                startupTask = run.Startup;
                executionTask = run.Execution;
            }
            catch
            {
                linkedCts.Dispose();
                throw;
            }

            _linkedCts = linkedCts;
            _executionTask = executionTask;
        }

        try
        {
            var firstCompletion = await Task
                .WhenAny(startupTask, executionTask)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ReferenceEquals(firstCompletion, executionTask))
            {
                await executionTask.ConfigureAwait(false);
                if (!startupTask.IsCompleted)
                {
                    throw new InvalidOperationException(
                        $"后台任务 {ServiceName} 在发出启动就绪信号前已结束。");
                }
            }

            await startupTask.ConfigureAwait(false);

            if (!executionTask.IsCompleted)
            {
                _ = ObserveExecutionAsync(executionTask, linkedCts, executionCancellationToken);
                return;
            }

            await executionTask.ConfigureAwait(false);
            ClearAttempt(executionTask, linkedCts);
        }
        catch
        {
            BeginAbortStartAttempt(executionTask, linkedCts);
            throw;
        }
    }

    private async Task ObserveExecutionAsync(
        Task executionTask,
        CancellationTokenSource linkedCts,
        CancellationToken executionCancellationToken)
    {
        Exception? failure = null;
        try
        {
            await executionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (executionCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            ClearAttempt(executionTask, linkedCts);
        }

        if (failure is not null && _logger is not null)
        {
            try
            {
                _logger.Error($"[后台任务] {ServiceName} 运行失败：{failure.Message}");
            }
            catch
            {
                // 故障观察本身不得制造第二个未观察后台异常。
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? executionTask;
        CancellationTokenSource? linkedCts;
        lock (_lifecycleSync)
        {
            executionTask = _executionTask;
            linkedCts = _linkedCts;
        }

        if (executionTask is null || linkedCts is null)
        {
            return;
        }

        List<Exception>? failures = null;
        try
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }

        try
        {
            await _task.StopAsync(cancellationToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }

        try
        {
            await executionTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            (failures ??= []).Add(ex);
        }

        if (executionTask.IsCompleted)
            ClearAttempt(executionTask, linkedCts);

        ThrowFailures(failures);
    }

    private void BeginAbortStartAttempt(
        Task executionTask,
        CancellationTokenSource linkedCts)
    {
        _ = CancelFailedStartAsync(linkedCts);
        _ = StopFailedStartAsync();
        _ = ObserveExecutionAsync(executionTask, linkedCts, linkedCts.Token);
    }

    private async Task CancelFailedStartAsync(CancellationTokenSource linkedCts)
    {
        try
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCleanupFailure("取消失败的启动尝试", ex);
        }
    }

    private async Task StopFailedStartAsync()
    {
        try
        {
            await _task.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCleanupFailure("停止失败的启动尝试", ex);
        }
    }

    private void LogCleanupFailure(string action, Exception exception)
    {
        if (_logger is null)
            return;

        try
        {
            _logger.Error($"[后台任务] {ServiceName} {action}：{exception.Message}");
        }
        catch
        {
            // 故障清理日志不得制造第二个未观察异常。
        }
    }

    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures is null or { Count: 0 })
            return;

        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(failures);
    }

    private void ClearAttempt(Task executionTask, CancellationTokenSource linkedCts)
    {
        if (TryDetachAttempt(executionTask, linkedCts))
        {
            linkedCts.Dispose();
        }
    }

    private bool TryDetachAttempt(Task executionTask, CancellationTokenSource linkedCts)
    {
        lock (_lifecycleSync)
        {
            if (ReferenceEquals(_executionTask, executionTask) &&
                ReferenceEquals(_linkedCts, linkedCts))
            {
                _executionTask = null;
                _linkedCts = null;
                return true;
            }
        }

        return false;
    }
}
