using IIoT.Edge.Application.Abstractions.Tasks;
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

    private sealed class DeadlineManagedService : IManagedBackgroundService
    {
        private readonly bool _completeStartWhenStopped;
        private readonly ICollection<string>? _stopOrder;
        private readonly Func<Task>? _stop;
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
            Func<Task>? stop = null)
        {
            ServiceName = serviceName;
            _completeStartWhenStopped = completeStartWhenStopped;
            _stopOrder = stopOrder;
            _stop = stop;
            if (!completeStartWhenStopped)
                _startRelease.TrySetResult();
        }

        public string ServiceName { get; }
        public int StartCallCount => Volatile.Read(ref _startCallCount);
        public int StopCallCount => Volatile.Read(ref _stopCallCount);
        public Task LateStopCompleted => _lateStopCompleted.Task;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startCallCount);
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
