using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Common.Tasks;
using Xunit;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class LongRunningBackgroundTaskGroupServiceBehaviorTests
{
    [Fact]
    public async Task Service_WhenCallerCancelsAfterNonBlockingStart_ShouldCleanAttemptAndAllowRestart()
    {
        var task = new RestartableBackgroundTask();
        var service = new LongRunningBackgroundTaskService(task);
        using var firstCancellation = new CancellationTokenSource();

        await service.StartAsync(firstCancellation.Token);
        await task.FirstInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await firstCancellation.CancelAsync();

        var firstExecution = await task.FirstExecution.WaitAsync(TestContext.Current.CancellationToken);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstExecution);
        Assert.True(actual.CancellationToken.IsCancellationRequested);
        await task.FirstInvocationStopped.WaitAsync(TestContext.Current.CancellationToken);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await task.SecondInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.InvocationCount);
        Assert.True(task.SecondInvocationStopped.IsCompleted);
    }

    [Fact]
    public async Task StartAsync_WhenTaskReturnsAlreadyFaultedTask_ShouldSurfaceExceptionAndAllowRestart()
    {
        var task = new AlreadyFaultedThenRunningBackgroundTask();
        var service = new LongRunningBackgroundTaskService(task);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("already faulted", exception.Message);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await task.SecondInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(task.SecondInvocationStopped.IsCompleted);
    }

    [Fact]
    public async Task StartAsync_WhenExecutionFaultsBeforeReadiness_ShouldSurfaceStartupFailure()
    {
        var task = new FaultBeforeStartupBackgroundTask();
        var status = new BackgroundServiceRuntimeStatusStore();
        var service = new LongRunningBackgroundTaskService(
            task,
            runtimeStatus: status);

        var start = service.StartAsync(TestContext.Current.CancellationToken);
        await task.ExecutionStarted.WaitAsync(TestContext.Current.CancellationToken);
        task.FailBeforeStartup();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => start);
        Assert.Equal("failed before startup", exception.Message);
        Assert.True(status.TryGet(task.TaskName, out var snapshot));
        Assert.Equal(BackgroundServiceRuntimeState.Faulted, snapshot.State);
        Assert.Equal("BACKGROUND_TASK_START_FAILED", snapshot.ErrorCode);
    }

    [Fact]
    public async Task StopAsync_AfterStartupFailureCleanup_ShouldKeepFaultedDiagnosticUntilRetry()
    {
        var task = new FaultBeforeStartupBackgroundTask();
        var status = new BackgroundServiceRuntimeStatusStore();
        var service = new LongRunningBackgroundTaskService(
            task,
            runtimeStatus: status);

        var start = service.StartAsync(TestContext.Current.CancellationToken);
        await task.ExecutionStarted.WaitAsync(TestContext.Current.CancellationToken);
        task.FailBeforeStartup();
        await Assert.ThrowsAsync<InvalidOperationException>(() => start);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(status.TryGet(task.TaskName, out var snapshot));
        Assert.Equal(BackgroundServiceRuntimeState.Faulted, snapshot.State);
        Assert.Equal("BACKGROUND_TASK_START_FAILED", snapshot.ErrorCode);
    }

    [Fact]
    public async Task Service_WhenTaskFaultsAfterNonBlockingStart_ShouldObserveFailureAndAllowRestart()
    {
        var task = new LaterFaultThenRunningBackgroundTask();
        var logger = new FakeLogService();
        var failureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.EntryAdded += entry =>
        {
            if (entry.Level == "Error"
                && entry.Message.Contains(nameof(InvalidOperationException), StringComparison.Ordinal))
            {
                failureLogged.TrySetResult();
            }
        };
        var service = new LongRunningBackgroundTaskService(task, logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        task.FailFirstInvocation();
        await failureLogged.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("later fault", StringComparison.Ordinal));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await task.SecondInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.InvocationCount);
        Assert.True(task.SecondInvocationStopped.IsCompleted);
    }

    [Fact]
    public async Task StartAsync_WhenTaskCompletesImmediately_ShouldPermitImmediateRestart()
    {
        var task = new ImmediatelyCompletingBackgroundTask();
        var service = new LongRunningBackgroundTaskService(task);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.InvocationCount);
    }

    [Fact]
    public void RuntimeStatusStore_ShouldUseCaseInsensitiveIdentityAndPublishOnlyTargetedChanges()
    {
        var store = new BackgroundServiceRuntimeStatusStore();
        var events = new List<BackgroundServiceRuntimeSnapshot>();
        store.Changed += (_, snapshot) => events.Add(snapshot);

        store.Set("ProcessQueueTask", BackgroundServiceRuntimeState.Starting);
        store.Set("processqueuetask", BackgroundServiceRuntimeState.Starting);
        store.Set("PROCESSQUEUETASK", BackgroundServiceRuntimeState.Running);
        store.Set("CloudRetryTask", BackgroundServiceRuntimeState.Faulted, "BACKGROUND_TASK_START_FAILED");

        Assert.Equal(3, events.Count);
        Assert.True(store.TryGet("processQueueTask", out var processQueue));
        Assert.Equal(BackgroundServiceRuntimeState.Running, processQueue.State);
        Assert.True(store.TryGet("cloudretrytask", out var cloudRetry));
        Assert.Equal("BACKGROUND_TASK_START_FAILED", cloudRetry.ErrorCode);
        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public async Task Service_WhenExecutionFaults_ShouldKeepSiblingIndependentAndPublishStableState()
    {
        var faultingTask = new LaterFaultThenRunningBackgroundTask();
        var siblingTask = new RestartableBackgroundTask();
        var status = new BackgroundServiceRuntimeStatusStore();
        var faultPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        status.Changed += (_, snapshot) =>
        {
            if (snapshot.ServiceName == faultingTask.TaskName
                && snapshot.State == BackgroundServiceRuntimeState.Faulted)
            {
                faultPublished.TrySetResult();
            }
        };
        var faultingService = new LongRunningBackgroundTaskService(
            faultingTask,
            runtimeStatus: status);
        var siblingService = new LongRunningBackgroundTaskService(
            siblingTask,
            runtimeStatus: status);

        await faultingService.StartAsync(TestContext.Current.CancellationToken);
        await siblingService.StartAsync(TestContext.Current.CancellationToken);
        faultingTask.FailFirstInvocation();
        await faultPublished.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(status.TryGet(faultingTask.TaskName, out var faulted));
        Assert.Equal(BackgroundServiceRuntimeState.Faulted, faulted.State);
        Assert.Equal("BACKGROUND_TASK_EXECUTION_FAULT", faulted.ErrorCode);
        Assert.True(status.TryGet(siblingTask.TaskName, out var sibling));
        Assert.Equal(BackgroundServiceRuntimeState.Running, sibling.State);

        await siblingService.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(status.TryGet(siblingTask.TaskName, out sibling));
        Assert.Equal(BackgroundServiceRuntimeState.Stopped, sibling.State);
    }

    [Fact]
    public async Task StartAsync_WhenChildFailsDuringStartup_ShouldSurfaceException()
    {
        var service = new LongRunningBackgroundTaskGroupService(
            "test-group",
            [new FailingBackgroundTask()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Equal("startup failed", exception.Message);
    }

    [Fact]
    public async Task StartAsync_WhenLaterChildFails_ShouldStopAlreadyRunningSibling()
    {
        var running = new ObservableBackgroundTask("running");
        var service = new LongRunningBackgroundTaskGroupService(
            "test-group",
            [running, new FailingBackgroundTask()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("startup failed", exception.Message);
        await running.Stopped.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, running.InvocationCount);
    }

    [Fact]
    public async Task StartAsync_WhenCallerCancelsInLaterChild_ShouldStopEarlierChildrenAndKeepCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        var first = new ObservableBackgroundTask("first");
        var canceling = new CancelOnStartBackgroundTask(cancellation);
        var service = new LongRunningBackgroundTaskGroupService(
            "test-group",
            [first, canceling]);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.StartAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        await first.Stopped.WaitAsync(TestContext.Current.CancellationToken);
        await canceling.Stopped.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopAsync_WhenLastChildFaults_ShouldStillStopAllChildrenInReverseOrder()
    {
        var stopOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var first = new ObservableBackgroundTask("first", stopOrder);
        var middle = new ObservableBackgroundTask("middle", stopOrder);
        var last = new FaultOnCancellationBackgroundTask("last", stopOrder);
        var service = new LongRunningBackgroundTaskGroupService(
            "test-group",
            [first, middle, last]);
        await service.StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal("last stop failed", exception.Message);
        Assert.Equal(["last", "middle", "first"], stopOrder.ToArray());
    }

    [Fact]
    public async Task Service_WhenCancellationCallbackFaultsDuringStop_ShouldClearStateAndAllowRestart()
    {
        var task = new FirstStopCallbackFaultTask();
        var service = new LongRunningBackgroundTaskService(task);
        await service.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.StopAsync(TestContext.Current.CancellationToken));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await task.SecondInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, task.InvocationCount);
    }

    [Fact]
    public async Task StopAsync_ShouldInvokeTaskStopHookBeforeCompleting()
    {
        var task = new StopHookBackgroundTask();
        var service = new LongRunningBackgroundTaskService(task);
        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, task.StopCallCount);
        Assert.True(task.ExecutionStopped.IsCompleted);
    }

    [Fact]
    public async Task StopAsync_WhenCallerCancelsWait_ShouldKeepZombieTrackedUntilItReallyCompletes()
    {
        var task = new IgnoreFirstCancellationBackgroundTask();
        var service = new LongRunningBackgroundTaskService(task);
        await service.StartAsync(TestContext.Current.CancellationToken);
        using var stopCancellation = new CancellationTokenSource();

        var stop = service.StopAsync(stopCancellation.Token);
        await task.StopHookCalled.WaitAsync(TestContext.Current.CancellationToken);
        await stopCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, task.InvocationCount);

        task.ReleaseFirstInvocation();
        await task.FirstInvocationStopped.WaitAsync(TestContext.Current.CancellationToken);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await task.SecondInvocationStarted.WaitAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.InvocationCount);
        Assert.True(task.SecondInvocationStopped.IsCompleted);
    }

    private abstract class TestStartupAwareBackgroundTask : IStartupAwareBackgroundTask
    {
        public abstract string TaskName { get; }

        public abstract Task StartAsync(CancellationToken ct);

        public virtual Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        public virtual BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            var execution = StartAsync(cancellationToken);
            return new BackgroundTaskRun(Task.CompletedTask, execution);
        }
    }

    private sealed class FailingBackgroundTask : TestStartupAwareBackgroundTask
    {
        public override string TaskName => "FailingTask";

        public override Task StartAsync(CancellationToken ct)
            => throw new InvalidOperationException("startup failed");
    }

    private sealed class RestartableBackgroundTask : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;
        private readonly TaskCompletionSource<Task> _firstExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstInvocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstInvocationStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocationStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => "RestartableTask";
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Task<Task> FirstExecution => _firstExecution.Task;
        public Task FirstInvocationStarted => _firstInvocationStarted.Task;
        public Task FirstInvocationStopped => _firstInvocationStopped.Task;
        public Task SecondInvocationStarted => _secondInvocationStarted.Task;
        public Task SecondInvocationStopped => _secondInvocationStopped.Task;

        public override Task StartAsync(CancellationToken ct)
        {
            var invocation = Interlocked.Increment(ref _invocationCount);
            var started = invocation == 1 ? _firstInvocationStarted : _secondInvocationStarted;
            var stopped = invocation == 1 ? _firstInvocationStopped : _secondInvocationStopped;
            var execution = ExecuteAsync(started, stopped, ct);
            if (invocation == 1)
            {
                _firstExecution.TrySetResult(execution);
            }

            return execution;
        }

        private static async Task ExecuteAsync(
            TaskCompletionSource started,
            TaskCompletionSource stopped,
            CancellationToken ct)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                stopped.TrySetResult();
            }
        }
    }

    private sealed class AlreadyFaultedThenRunningBackgroundTask : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;
        private readonly TaskCompletionSource _secondInvocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocationStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => "already-faulted";
        public Task SecondInvocationStarted => _secondInvocationStarted.Task;
        public Task SecondInvocationStopped => _secondInvocationStopped.Task;

        public override Task StartAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
            {
                return Task.FromException(new InvalidOperationException("already faulted"));
            }

            return RunSecondInvocationAsync(ct);
        }

        private async Task RunSecondInvocationAsync(CancellationToken ct)
        {
            _secondInvocationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _secondInvocationStopped.TrySetResult();
            }
        }
    }

    private sealed class FaultBeforeStartupBackgroundTask : IStartupAwareBackgroundTask
    {
        private readonly TaskCompletionSource _executionStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _failure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _startup =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string TaskName => "fault-before-startup";
        public Task ExecutionStarted => _executionStarted.Task;

        public void FailBeforeStartup() => _failure.TrySetException(
            new InvalidOperationException("failed before startup"));

        public Task StartAsync(CancellationToken ct)
            => StartWithStartup(ct).Execution;

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
            => new(_startup.Task, RunAsync(cancellationToken));

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            _executionStarted.TrySetResult();
            await _failure.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class LaterFaultThenRunningBackgroundTask : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;
        private readonly TaskCompletionSource _firstFailure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondInvocationStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => "later-fault";
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Task SecondInvocationStarted => _secondInvocationStarted.Task;
        public Task SecondInvocationStopped => _secondInvocationStopped.Task;

        public void FailFirstInvocation() => _firstFailure.TrySetException(
            new InvalidOperationException("later fault"));

        public override Task StartAsync(CancellationToken ct)
            => Interlocked.Increment(ref _invocationCount) == 1
                ? _firstFailure.Task
                : RunSecondInvocationAsync(ct);

        private async Task RunSecondInvocationAsync(CancellationToken ct)
        {
            _secondInvocationStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _secondInvocationStopped.TrySetResult();
            }
        }
    }

    private sealed class ImmediatelyCompletingBackgroundTask : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;

        public override string TaskName => "immediate";
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public override Task StartAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.CompletedTask;
        }
    }

    private sealed class ObservableBackgroundTask(
        string name,
        System.Collections.Concurrent.ConcurrentQueue<string>? stopOrder = null) : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;
        private readonly TaskCompletionSource _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => name;
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Task Stopped => _stopped.Task;

        public override async Task StartAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _invocationCount);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                stopOrder?.Enqueue(name);
                _stopped.TrySetResult();
            }
        }
    }

    private sealed class CancelOnStartBackgroundTask(CancellationTokenSource cancellation) : TestStartupAwareBackgroundTask
    {
        private readonly TaskCompletionSource _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => "canceling";
        public Task Stopped => _stopped.Task;

        public override Task StartAsync(CancellationToken ct)
            => StartWithStartup(ct).Execution;

        public override BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new BackgroundTaskRun(startup.Task, RunAsync(cancellationToken, startup));
        }

        private async Task RunAsync(CancellationToken ct, TaskCompletionSource startup)
        {
            try
            {
                await cancellation.CancelAsync();
                ct.ThrowIfCancellationRequested();
                startup.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _stopped.TrySetResult();
            }
        }
    }

    private sealed class FaultOnCancellationBackgroundTask(
        string name,
        System.Collections.Concurrent.ConcurrentQueue<string> stopOrder) : TestStartupAwareBackgroundTask
    {
        public override string TaskName => name;

        public override async Task StartAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                stopOrder.Enqueue(name);
                throw new InvalidOperationException($"{name} stop failed");
            }
        }
    }

    private sealed class FirstStopCallbackFaultTask : TestStartupAwareBackgroundTask
    {
        private int _invocationCount;
        private readonly TaskCompletionSource _secondInvocationStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override string TaskName => "callback-fault";
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Task SecondInvocationStarted => _secondInvocationStarted.Task;

        public override async Task StartAsync(CancellationToken ct)
        {
            var invocation = Interlocked.Increment(ref _invocationCount);
            using var registration = invocation == 1
                ? ct.Register(static () => throw new InvalidOperationException("cancel callback failed"))
                : default;
            if (invocation == 2)
            {
                _secondInvocationStarted.TrySetResult();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class StopHookBackgroundTask : TestStartupAwareBackgroundTask
    {
        private readonly TaskCompletionSource _executionStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopCallCount;

        public override string TaskName => "stop-hook";
        public int StopCallCount => Volatile.Read(ref _stopCallCount);
        public Task ExecutionStopped => _executionStopped.Task;

        public override async Task StartAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _executionStopped.TrySetResult();
            }
        }

        public override Task StopAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _stopCallCount);
            return Task.CompletedTask;
        }
    }

    private sealed class IgnoreFirstCancellationBackgroundTask : TestStartupAwareBackgroundTask
    {
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopHookCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _invocationCount;

        public override string TaskName => "ignore-first-cancellation";
        public int InvocationCount => Volatile.Read(ref _invocationCount);
        public Task FirstInvocationStopped => _firstStopped.Task;
        public Task SecondInvocationStarted => _secondStarted.Task;
        public Task SecondInvocationStopped => _secondStopped.Task;
        public Task StopHookCalled => _stopHookCalled.Task;

        public void ReleaseFirstInvocation() => _releaseFirst.TrySetResult();

        public override async Task StartAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _invocationCount) == 1)
            {
                await _releaseFirst.Task.ConfigureAwait(false);
                _firstStopped.TrySetResult();
                return;
            }

            _secondStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                _secondStopped.TrySetResult();
            }
        }

        public override Task StopAsync(CancellationToken ct)
        {
            _stopHookCalled.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
