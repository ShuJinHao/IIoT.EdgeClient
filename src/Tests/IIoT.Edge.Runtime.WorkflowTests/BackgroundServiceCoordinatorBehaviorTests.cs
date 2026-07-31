using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Common.Tasks;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class BackgroundServiceCoordinatorBehaviorTests
{
    [Fact]
    public async Task StartAsync_WhenServiceMissesDeadline_ShouldContinueBatchAndStopLateStart()
    {
        var timedOut = new DeadlineManagedService("TimedOut", completeStartWhenStopped: true);
        var following = new DeadlineManagedService("Following");
        var coordinator = new BackgroundServiceCoordinator(
            [timedOut, following],
            new FakeLogService(),
            new BackgroundServiceCoordinatorOptions
            {
                StartupTimeout = TimeSpan.Zero,
                StopTimeout = TimeSpan.FromSeconds(1)
            });

        var exception = await Assert.ThrowsAsync<BackgroundServiceStartException>(
            () => coordinator.StartAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(exception.Failures);
        Assert.Equal("TimedOut", failure.ServiceName);
        Assert.IsType<TimeoutException>(failure.Exception);
        Assert.Equal(1, following.StartCallCount);
        await timedOut.LateStopCompleted.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, timedOut.StopCallCount);

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, following.StopCallCount);
    }

    [Fact]
    public async Task StopAsync_WhenServicesFaultOrTimeout_ShouldAttemptAllInReverseAndAggregate()
    {
        var stopOrder = new List<string>();
        var first = new DeadlineManagedService("First", stopOrder: stopOrder);
        var hanging = new DeadlineManagedService(
            "Hanging",
            stopOrder: stopOrder,
            stop: static () => new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously).Task);
        var faulting = new DeadlineManagedService(
            "Faulting",
            stopOrder: stopOrder,
            stop: static () => Task.FromException(new InvalidOperationException("stop failed")));
        var coordinator = new BackgroundServiceCoordinator(
            [first, hanging, faulting],
            new FakeLogService(),
            new BackgroundServiceCoordinatorOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.Zero
            });
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<BackgroundServiceStopException>(
            () => coordinator.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["Faulting", "Hanging"], exception.Failures.Select(static failure => failure.ServiceName));
        Assert.IsType<InvalidOperationException>(exception.Failures[0].Exception);
        Assert.IsType<TimeoutException>(exception.Failures[1].Exception);
        Assert.Equal(["Faulting", "Hanging", "First"], stopOrder);
        Assert.Equal(1, first.StopCallCount);
        Assert.Equal(1, hanging.StopCallCount);
        Assert.Equal(1, faulting.StopCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenOneDataPipelineTaskFails_ShouldStillStartOtherTwoIndependently()
    {
        const string sensitiveMessage = "local path and token must not leak";
        var processQueue = new DeadlineManagedService(
            "ProcessQueueTask",
            start: () => Task.FromException(new InvalidOperationException(sensitiveMessage)));
        var cloudRetry = new DeadlineManagedService("CloudRetryTask");
        var mesRetry = new DeadlineManagedService("MesRetryTask");
        var logger = new FakeLogService();
        var coordinator = new BackgroundServiceCoordinator(
            [processQueue, cloudRetry, mesRetry],
            logger);

        var exception = await Assert.ThrowsAsync<BackgroundServiceStartException>(
            () => coordinator.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal("ProcessQueueTask", Assert.Single(exception.Failures).ServiceName);
        Assert.Equal(1, processQueue.StartCallCount);
        Assert.Equal(1, cloudRetry.StartCallCount);
        Assert.Equal(1, mesRetry.StartCallCount);
        Assert.Equal(1, processQueue.StopCallCount);
        Assert.DoesNotContain(sensitiveMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains(sensitiveMessage, StringComparison.Ordinal));

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, cloudRetry.StopCallCount);
        Assert.Equal(1, mesRetry.StopCallCount);
    }

    [Fact]
    public async Task RecoverySupervisor_WhenPipelineWorkerStartupFails_ShouldRetryOnlyFaultedWorker()
    {
        var runtimeStatus = new BackgroundServiceRuntimeStatusStore();
        var processQueue = new RecoverableManagedService(
            "ProcessQueueTask",
            runtimeStatus,
            failFirstStart: true);
        var cloudRetry = new RecoverableManagedService(
            "CloudRetryTask",
            runtimeStatus);
        var coordinator = new BackgroundServiceCoordinator(
            [processQueue, cloudRetry],
            new FakeLogService(),
            new BackgroundServiceCoordinatorOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.FromSeconds(1),
                RecoveryInterval = TimeSpan.FromMilliseconds(10)
            },
            runtimeStatus);

        var exception = await Assert.ThrowsAsync<BackgroundServiceStartException>(
            () => coordinator.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ProcessQueueTask", Assert.Single(exception.Failures).ServiceName);

        await processQueue.Recovered.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, processQueue.StartCallCount);
        Assert.Equal(1, cloudRetry.StartCallCount);
        Assert.True(runtimeStatus.TryGet("ProcessQueueTask", out var recovered));
        Assert.Equal(BackgroundServiceRuntimeState.Running, recovered.State);

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecoverySupervisor_WhenPipelineWorkerFaultsAfterReady_ShouldRestartOnlyThatWorker()
    {
        var runtimeStatus = new BackgroundServiceRuntimeStatusStore();
        var processQueue = new RecoverableManagedService(
            "ProcessQueueTask",
            runtimeStatus);
        var mesRetry = new RecoverableManagedService(
            "MesRetryTask",
            runtimeStatus);
        var coordinator = new BackgroundServiceCoordinator(
            [processQueue, mesRetry],
            new FakeLogService(),
            new BackgroundServiceCoordinatorOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.FromSeconds(1),
                RecoveryInterval = TimeSpan.FromMilliseconds(10)
            },
            runtimeStatus);
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        processQueue.FailAfterReady();
        await processQueue.Recovered.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, processQueue.StartCallCount);
        Assert.Equal(1, mesRetry.StartCallCount);
        Assert.True(runtimeStatus.TryGet("ProcessQueueTask", out var recovered));
        Assert.Equal(BackgroundServiceRuntimeState.Running, recovered.State);

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecoverySupervisor_WhenStartupCleanupTimesOut_ShouldWaitForLateStopThenRetry()
    {
        var runtimeStatus = new BackgroundServiceRuntimeStatusStore();
        var processQueue = new CleanupTimeoutManagedService(runtimeStatus);
        var coordinator = new BackgroundServiceCoordinator(
            [processQueue],
            new FakeLogService(),
            new BackgroundServiceCoordinatorOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.FromMilliseconds(20),
                RecoveryInterval = TimeSpan.FromMilliseconds(10)
            },
            runtimeStatus);

        await Assert.ThrowsAsync<BackgroundServiceStartException>(
            () => coordinator.StartAsync(TestContext.Current.CancellationToken));

        Assert.True(runtimeStatus.TryGet(processQueue.ServiceName, out var timedOut));
        Assert.Equal(BackgroundServiceRuntimeState.Faulted, timedOut.State);
        Assert.Equal("BACKGROUND_TASK_STOP_TIMEOUT", timedOut.ErrorCode);
        var timeoutRepublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtimeStatus.Changed += (_, snapshot) =>
        {
            if (snapshot.ServiceName == processQueue.ServiceName
                && snapshot.State == BackgroundServiceRuntimeState.Faulted
                && snapshot.ErrorCode == "BACKGROUND_TASK_STOP_TIMEOUT")
            {
                timeoutRepublished.TrySetResult();
            }
        };
        runtimeStatus.Set(
            processQueue.ServiceName,
            BackgroundServiceRuntimeState.Stopped);
        await timeoutRepublished.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, processQueue.StartCallCount);

        processQueue.ReleaseTimedOutStop();
        await processQueue.Recovered.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, processQueue.StartCallCount);
        Assert.True(runtimeStatus.TryGet(processQueue.ServiceName, out var recovered));
        Assert.Equal(BackgroundServiceRuntimeState.Running, recovered.State);

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class RecoverableManagedService(
        string serviceName,
        BackgroundServiceRuntimeStatusStore runtimeStatus,
        bool failFirstStart = false) : IManagedBackgroundService
    {
        private readonly TaskCompletionSource _recovered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCallCount;
        private int _stopCallCount;

        public string ServiceName { get; } = serviceName;
        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public int StopCallCount => Volatile.Read(ref _stopCallCount);
        public Task Recovered => _recovered.Task;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = Interlocked.Increment(ref _startCallCount);
            if (failFirstStart && call == 1)
            {
                runtimeStatus.Set(
                    ServiceName,
                    BackgroundServiceRuntimeState.Faulted,
                    "BACKGROUND_TASK_START_FAILED");
                return Task.FromException(new InvalidOperationException("startup failed"));
            }

            runtimeStatus.Set(ServiceName, BackgroundServiceRuntimeState.Running);
            if (call > 1)
            {
                _recovered.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCallCount);
            if (StartCallCount > 1 || !failFirstStart)
            {
                runtimeStatus.Set(ServiceName, BackgroundServiceRuntimeState.Stopped);
            }
            return Task.CompletedTask;
        }

        public void FailAfterReady()
            => runtimeStatus.Set(
                ServiceName,
                BackgroundServiceRuntimeState.Faulted,
                "BACKGROUND_TASK_EXECUTION_FAULT");
    }

    private sealed class CleanupTimeoutManagedService(
        BackgroundServiceRuntimeStatusStore runtimeStatus)
        : IManagedBackgroundService
    {
        private readonly TaskCompletionSource _releaseTimedOutStop =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _recovered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCallCount;
        private int _stopCallCount;

        public string ServiceName => "ProcessQueueTask";
        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public Task Recovered => _recovered.Task;

        public void ReleaseTimedOutStop() => _releaseTimedOutStop.TrySetResult();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = Interlocked.Increment(ref _startCallCount);
            if (call == 1)
            {
                runtimeStatus.Set(
                    ServiceName,
                    BackgroundServiceRuntimeState.Faulted,
                    "BACKGROUND_TASK_START_FAILED");
                return Task.FromException(new InvalidOperationException("startup failed"));
            }

            runtimeStatus.Set(ServiceName, BackgroundServiceRuntimeState.Running);
            _recovered.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _stopCallCount) == 1)
            {
                runtimeStatus.Set(ServiceName, BackgroundServiceRuntimeState.Stopping);
                await _releaseTimedOutStop.Task.WaitAsync(cancellationToken);
            }

            runtimeStatus.Set(ServiceName, BackgroundServiceRuntimeState.Stopped);
        }
    }

    private sealed class DeadlineManagedService : IManagedBackgroundService
    {
        private readonly bool _completeStartWhenStopped;
        private readonly ICollection<string>? _stopOrder;
        private readonly Func<Task>? _stop;
        private readonly Func<Task>? _start;
        private readonly TaskCompletionSource _startRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _lateStopCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCallCount;
        private int _stopCallCount;

        public DeadlineManagedService(
            string serviceName,
            bool completeStartWhenStopped = false,
            ICollection<string>? stopOrder = null,
            Func<Task>? stop = null,
            Func<Task>? start = null)
        {
            ServiceName = serviceName;
            _completeStartWhenStopped = completeStartWhenStopped;
            _stopOrder = stopOrder;
            _stop = stop;
            _start = start;
            if (!completeStartWhenStopped && start is null)
                _startRelease.TrySetResult();
        }

        public string ServiceName { get; }
        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public int StopCallCount => Volatile.Read(ref _stopCallCount);
        public Task LateStopCompleted => _lateStopCompleted.Task;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCallCount);
            if (_start is not null)
            {
                await _start().WaitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await _startRelease.Task.ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _stopCallCount);
            _stopOrder?.Add(ServiceName);
            if (_completeStartWhenStopped)
            {
                _startRelease.TrySetResult();
                if (call >= 2)
                    _lateStopCompleted.TrySetResult();
            }

            return _stop?.Invoke() ?? Task.CompletedTask;
        }
    }
}
