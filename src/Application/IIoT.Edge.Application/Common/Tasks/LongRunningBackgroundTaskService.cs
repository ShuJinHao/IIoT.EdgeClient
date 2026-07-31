using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Module.Contracts.Logging;

namespace IIoT.Edge.Application.Common.Tasks;

public sealed class LongRunningBackgroundTaskService : IManagedBackgroundService
{
    private readonly IStartupAwareBackgroundTask _task;
    private readonly ILogService? _logger;
    private readonly IBackgroundServiceRuntimeStatusWriter? _runtimeStatus;
    private readonly object _lifecycleSync = new();
    private CancellationTokenSource? _linkedCts;
    private Task? _executionTask;
    private bool _startupFailed;

    public LongRunningBackgroundTaskService(
        IBackgroundTask task,
        ILogService? logger = null,
        IBackgroundServiceRuntimeStatusWriter? runtimeStatus = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        _task = task as IStartupAwareBackgroundTask
            ?? throw new ArgumentException(
                $"长运行后台任务 {task.TaskName} 必须实现 {nameof(IStartupAwareBackgroundTask)} 显式启动握手。",
                nameof(task));
        _logger = logger;
        _runtimeStatus = runtimeStatus;
    }

    public string ServiceName => _task.TaskName;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetRuntimeStatus(BackgroundServiceRuntimeState.Starting);
        CancellationTokenSource linkedCts;
        Task executionTask;
        Task startupTask;
        CancellationToken executionCancellationToken;
        lock (_lifecycleSync)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                SetRuntimeStatus(BackgroundServiceRuntimeState.Stopped);
                throw;
            }
            if (_executionTask is not null)
            {
                if (!_executionTask.IsCompleted)
                {
                    SetRuntimeStatus(BackgroundServiceRuntimeState.Running);
                    return;
                }

                _executionTask = null;
                _linkedCts?.Dispose();
                _linkedCts = null;
            }

            _startupFailed = false;
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
                _startupFailed = true;
                linkedCts.Dispose();
                SetRuntimeStatus(
                    BackgroundServiceRuntimeState.Faulted,
                    "BACKGROUND_TASK_START_FAILED");
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
                SetRuntimeStatus(BackgroundServiceRuntimeState.Running);
                _ = ObserveExecutionAsync(executionTask, linkedCts, executionCancellationToken);
                return;
            }

            await executionTask.ConfigureAwait(false);
            ClearAttempt(executionTask, linkedCts);
            PublishExecutionCompletionStatus(
                failure: null,
                executionCancellationToken.IsCancellationRequested);
        }
        catch
        {
            lock (_lifecycleSync)
            {
                _startupFailed = true;
            }
            SetRuntimeStatus(
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_START_FAILED");
            BeginAbortStartAttempt(executionTask, linkedCts);
            throw;
        }
    }

    private async Task ObserveExecutionAsync(
        Task executionTask,
        CancellationTokenSource linkedCts,
        CancellationToken executionCancellationToken,
        bool publishCompletionStatus = true)
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
                _logger.Error($"[后台任务] {ServiceName} 运行失败（{failure.GetType().Name}）。");
            }
            catch
            {
                // 故障观察本身不得制造第二个未观察后台异常。
            }
        }

        if (publishCompletionStatus)
            PublishExecutionCompletionStatus(
                failure,
                executionCancellationToken.IsCancellationRequested);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? executionTask;
        CancellationTokenSource? linkedCts;
        bool preserveStartupFailure;
        lock (_lifecycleSync)
        {
            executionTask = _executionTask;
            linkedCts = _linkedCts;
            preserveStartupFailure = _startupFailed;
        }

        if (executionTask is null || linkedCts is null)
        {
            if (!preserveStartupFailure)
            {
                SetRuntimeStatus(BackgroundServiceRuntimeState.Stopped);
            }
            return;
        }

        SetRuntimeStatus(BackgroundServiceRuntimeState.Stopping);
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

        if (failures is not null and { Count: > 0 })
        {
            SetRuntimeStatus(
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_STOP_FAILED");
        }
        else if (preserveStartupFailure)
        {
            SetRuntimeStatus(
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_START_FAILED");
        }
        else
        {
            SetRuntimeStatus(BackgroundServiceRuntimeState.Stopped);
        }
        ThrowFailures(failures);
    }

    private void BeginAbortStartAttempt(
        Task executionTask,
        CancellationTokenSource linkedCts)
    {
        _ = CancelFailedStartAsync(linkedCts);
        _ = StopFailedStartAsync();
        _ = ObserveExecutionAsync(
            executionTask,
            linkedCts,
            linkedCts.Token,
            publishCompletionStatus: false);
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
            _logger.Error($"[后台任务] {ServiceName} {action}（{exception.GetType().Name}）。");
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

    private void SetRuntimeStatus(
        BackgroundServiceRuntimeState state,
        string? errorCode = null)
    {
        try
        {
            _runtimeStatus?.Set(ServiceName, state, errorCode);
        }
        catch (Exception ex)
        {
            LogCleanupFailure("更新诊断状态失败", ex);
        }
    }

    private void PublishExecutionCompletionStatus(
        Exception? failure,
        bool cancellationRequested)
    {
        if (failure is not null)
        {
            SetRuntimeStatus(
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_EXECUTION_FAULT");
            return;
        }

        if (cancellationRequested)
        {
            SetRuntimeStatus(BackgroundServiceRuntimeState.Stopped);
            return;
        }

        if (_logger is not null)
        {
            try
            {
                _logger.Error($"[后台任务] {ServiceName} 未收到停止请求即结束。");
            }
            catch
            {
                // 故障观察本身不得制造第二个未观察后台异常。
            }
        }

        SetRuntimeStatus(
            BackgroundServiceRuntimeState.Faulted,
            "BACKGROUND_TASK_UNEXPECTED_EXIT");
    }
}
