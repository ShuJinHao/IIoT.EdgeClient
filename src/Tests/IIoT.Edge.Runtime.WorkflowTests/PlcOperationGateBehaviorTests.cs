using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using Microsoft.Extensions.Time.Testing;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class PlcOperationGateBehaviorTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(1);

    [Fact]
    public void SchedulingAgingInterval_ShouldClampToFiveHundredMillisecondsThroughFiveSeconds()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(500),
            PlcOperationSchedulingContext.ClampAgingInterval(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(
            TimeSpan.FromSeconds(2),
            PlcOperationSchedulingContext.ClampAgingInterval(TimeSpan.FromSeconds(2)));
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            PlcOperationSchedulingContext.ClampAgingInterval(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task QueuedOperations_ShouldHonorPriorityAndFifoWithinSameLevel()
    {
        var gate = new PlcOperationGate("FakePLC", LongTimeout, LongTimeout);
        var activeStarted = NewCompletion();
        var activeCompletion = NewCompletion<int>();
        var executionOrder = new List<string>();
        var executionSync = new object();

        var active = gate.ExecuteAsync(
            "Active",
            _ =>
            {
                activeStarted.TrySetResult();
                return activeCompletion.Task;
            },
            LongTimeout,
            static () => Task.CompletedTask,
            static _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await activeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        var periodic = Queue(PlcOperationPriority.Periodic, "Periodic");
        var businessFirst = Queue(PlcOperationPriority.BusinessOnDemand, "Business-1");
        var businessSecond = Queue(PlcOperationPriority.BusinessOnDemand, "Business-2");
        var interactive = Queue(PlcOperationPriority.Interactive, "Interactive");

        activeCompletion.TrySetResult(0);
        await Task.WhenAll(active, periodic, businessFirst, businessSecond, interactive)
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Interactive", "Business-1", "Business-2", "Periodic"],
            executionOrder);
        await gate.DisposeAsync(static () => Task.CompletedTask);
        return;

        Task<int> Queue(PlcOperationPriority priority, string operationName)
        {
            using var scheduling = PlcOperationSchedulingContext.Push(
                priority,
                TimeSpan.FromSeconds(1));
            return gate.ExecuteAsync(
                operationName,
                _ =>
                {
                    lock (executionSync)
                    {
                        executionOrder.Add(operationName);
                    }

                    return Task.FromResult(0);
                },
                LongTimeout,
                static () => Task.CompletedTask,
                static _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task PeriodicWaiter_ShouldAgeToInteractiveAndAvoidStarvation()
    {
        var timeProvider = new FakeTimeProvider();
        var gate = new PlcOperationGate("FakePLC", LongTimeout, LongTimeout, timeProvider);
        var activeStarted = NewCompletion();
        var activeCompletion = NewCompletion<int>();
        var executionOrder = new List<string>();

        var active = gate.ExecuteAsync(
            "Active",
            _ =>
            {
                activeStarted.TrySetResult();
                return activeCompletion.Task;
            },
            LongTimeout,
            static () => Task.CompletedTask,
            static _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await activeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Task<int> periodic;
        using (PlcOperationSchedulingContext.Push(
                   PlcOperationPriority.Periodic,
                   TimeSpan.FromMilliseconds(500)))
        {
            periodic = Queue("Periodic");
        }

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        var interactive = new List<Task<int>>();
        for (var index = 1; index <= 3; index++)
        {
            using var scheduling = PlcOperationSchedulingContext.Push(
                PlcOperationPriority.Interactive,
                TimeSpan.FromMilliseconds(500));
            interactive.Add(Queue($"Interactive-{index}"));
        }

        activeCompletion.TrySetResult(0);
        await Task.WhenAll([active, periodic, .. interactive])
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Periodic", "Interactive-1", "Interactive-2", "Interactive-3"],
            executionOrder);
        await gate.DisposeAsync(static () => Task.CompletedTask);
        return;

        Task<int> Queue(string operationName)
            => gate.ExecuteAsync(
                operationName,
                _ =>
                {
                    executionOrder.Add(operationName);
                    return Task.FromResult(0);
                },
                LongTimeout,
                static () => Task.CompletedTask,
                static _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DifferentPlcGates_ShouldExecuteInParallel()
    {
        var firstGate = new PlcOperationGate("PLC-1", LongTimeout, LongTimeout);
        var secondGate = new PlcOperationGate("PLC-2", LongTimeout, LongTimeout);
        var release = NewCompletion();
        var firstStarted = NewCompletion();
        var secondStarted = NewCompletion();

        var first = Start(firstGate, firstStarted);
        var second = Start(secondGate, secondStarted);
        await Task.WhenAll(firstStarted.Task, secondStarted.Task)
            .WaitAsync(TestContext.Current.CancellationToken);

        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);
        await firstGate.DisposeAsync(static () => Task.CompletedTask);
        await secondGate.DisposeAsync(static () => Task.CompletedTask);
        return;

        Task<int> Start(PlcOperationGate gate, TaskCompletionSource started)
            => gate.ExecuteAsync(
                "Read",
                async _ =>
                {
                    started.TrySetResult();
                    await release.Task.ConfigureAwait(false);
                    return 0;
                },
                LongTimeout,
                static () => Task.CompletedTask,
                static _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CallerCancellation_WhenAbortThrows_ShouldObserveProtocolBeforeQuarantine()
    {
        var gate = new PlcOperationGate("FakePLC", LongTimeout, LongTimeout);
        var operationStarted = NewCompletion();
        var protocolCompletion = NewCompletion<int>();
        var abortCount = 0;
        var releaseCount = 0;
        using var callerCancellation = new CancellationTokenSource();

        var operationTask = gate.ExecuteAsync(
            "Read",
            _ =>
            {
                operationStarted.TrySetResult();
                return protocolCompletion.Task;
            },
            LongTimeout,
            () =>
            {
                Interlocked.Increment(ref abortCount);
                return Task.FromException(new IOException("abort failed"));
            },
            _ =>
            {
                Interlocked.Increment(ref releaseCount);
                return Task.CompletedTask;
            },
            callerCancellation.Token);

        await operationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();
        Assert.False(operationTask.IsCompleted);

        protocolCompletion.TrySetResult(7);
        var exception = await Assert.ThrowsAsync<PlcServiceQuarantinedException>(
            () => operationTask.WaitAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PlcServiceQuarantinedException.StableReasonCode, exception.ReasonCode);
        Assert.Equal(1, Volatile.Read(ref abortCount));
        Assert.Equal(1, Volatile.Read(ref releaseCount));
        await gate.DisposeAsync(static () => Task.CompletedTask);
    }

    [Fact]
    public async Task AbortNeverCompletes_ShouldRetainLeaseAndResourcesUntilSettlement()
    {
        var hardBound = TimeSpan.FromMilliseconds(100);
        var gate = new PlcOperationGate("FakePLC", hardBound, hardBound);
        var operationStarted = NewCompletion();
        var protocolCompletion = NewCompletion<int>();
        var abortCompletion = NewCompletion();
        var releaseAfterAbortCount = 0;
        var resourceReleaseCount = 0;
        using var callerCancellation = new CancellationTokenSource();

        var operationTask = gate.ExecuteAsync(
            "Read",
            _ =>
            {
                operationStarted.TrySetResult();
                return protocolCompletion.Task;
            },
            LongTimeout,
            () => abortCompletion.Task,
            _ =>
            {
                Interlocked.Increment(ref releaseAfterAbortCount);
                return Task.CompletedTask;
            },
            callerCancellation.Token);

        await operationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();
        protocolCompletion.TrySetResult(1);

        await Assert.ThrowsAsync<PlcServiceQuarantinedException>(
            () => operationTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, Volatile.Read(ref releaseAfterAbortCount));

        var replacementFactoryCalled = false;
        await Assert.ThrowsAsync<PlcServiceQuarantinedException>(() => gate.ExecuteAsync(
            "Replacement",
            _ =>
            {
                replacementFactoryCalled = true;
                return Task.FromResult(0);
            },
            LongTimeout,
            static () => Task.CompletedTask,
            static _ => Task.CompletedTask,
            TestContext.Current.CancellationToken));
        Assert.False(replacementFactoryCalled);

        await Assert.ThrowsAsync<PlcServiceQuarantinedException>(
            () => gate.DisposeAsync(() =>
            {
                Interlocked.Increment(ref resourceReleaseCount);
                return Task.CompletedTask;
            }).AsTask());
        Assert.Equal(0, Volatile.Read(ref resourceReleaseCount));

        abortCompletion.TrySetResult();
        await gate.DisposeAsync(() =>
        {
            Interlocked.Increment(ref resourceReleaseCount);
            return Task.CompletedTask;
        });

        Assert.Equal(1, Volatile.Read(ref releaseAfterAbortCount));
        Assert.Equal(1, Volatile.Read(ref resourceReleaseCount));
    }

    [Fact]
    public async Task DisposeAsync_ShouldCancelQueuedWaiterAndReleaseOnlyAfterActiveLeaseSettles()
    {
        var gate = new PlcOperationGate("FakePLC", LongTimeout, LongTimeout);
        var activeStarted = NewCompletion();
        var activeProtocol = NewCompletion<int>();
        var activeAbortCount = 0;
        var activeReleaseCount = 0;
        var resourceReleaseCount = 0;
        var waiterFactoryCalled = false;

        var activeTask = gate.ExecuteAsync(
            "ActiveRead",
            _ =>
            {
                activeStarted.TrySetResult();
                return activeProtocol.Task;
            },
            LongTimeout,
            () =>
            {
                Interlocked.Increment(ref activeAbortCount);
                return Task.CompletedTask;
            },
            _ =>
            {
                Interlocked.Increment(ref activeReleaseCount);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
        await activeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        var waiterTask = gate.ExecuteAsync(
            "QueuedWrite",
            _ =>
            {
                waiterFactoryCalled = true;
                return Task.FromResult(0);
            },
            LongTimeout,
            static () => Task.CompletedTask,
            static _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        var disposeTask = gate.DisposeAsync(() =>
        {
            Interlocked.Increment(ref resourceReleaseCount);
            return Task.CompletedTask;
        }).AsTask();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => waiterTask.WaitAsync(TestContext.Current.CancellationToken));
        Assert.False(waiterFactoryCalled);
        Assert.False(disposeTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref resourceReleaseCount));

        activeProtocol.TrySetResult(9);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => activeTask.WaitAsync(TestContext.Current.CancellationToken));
        await disposeTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref activeAbortCount));
        Assert.Equal(1, Volatile.Read(ref activeReleaseCount));
        Assert.Equal(1, Volatile.Read(ref resourceReleaseCount));
    }

    [Fact]
    public async Task LateConstructedTransport_AfterCancellation_ShouldNotReviveAndShouldReleaseOnce()
    {
        var gate = new PlcOperationGate("FakeMC", LongTimeout, LongTimeout);
        var constructorStarted = NewCompletion();
        var constructorCompletion = NewCompletion();
        PlcTransportOwner<CountingTransport>? contextOwner = null;
        PlcTransportOwner<CountingTransport>? liveOwner = null;
        var isConnected = false;
        var transport = new CountingTransport();
        using var callerCancellation = new CancellationTokenSource();

        var connectTask = gate.ExecuteAsync(
            "Connect",
            async token =>
            {
                constructorStarted.TrySetResult();
                await constructorCompletion.Task.ConfigureAwait(false);
                contextOwner = new PlcTransportOwner<CountingTransport>(
                    transport,
                    static value => value.Dispose());
                token.ThrowIfCancellationRequested();
                liveOwner = contextOwner;
                isConnected = true;
                return true;
            },
            LongTimeout,
            () => contextOwner is null
                ? Task.CompletedTask
                : Task.Run(contextOwner.Release),
            _ => Task.Run(() =>
            {
                if (ReferenceEquals(liveOwner, contextOwner))
                {
                    liveOwner = null;
                    isConnected = false;
                }

                contextOwner?.Release();
            }),
            callerCancellation.Token);

        await constructorStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();
        Assert.Null(contextOwner);

        constructorCompletion.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connectTask.WaitAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(contextOwner);
        Assert.False(contextOwner!.IsAvailable);
        Assert.Null(liveOwner);
        Assert.False(isConnected);
        Assert.Equal(1, transport.DisposeCount);
        await gate.DisposeAsync(static () => Task.CompletedTask);
    }

    [Fact]
    public async Task TransportOwner_WhenReleasedConcurrently_ShouldDisposeExactlyOnce()
    {
        var transport = new CountingTransport();
        var owner = new PlcTransportOwner<CountingTransport>(
            transport,
            static value => value.Dispose());
        var release = NewCompletion();

        async Task ReleaseAsync()
        {
            await release.Task.ConfigureAwait(false);
            owner.Release();
        }

        var first = Task.Run(ReleaseAsync, TestContext.Current.CancellationToken);
        var second = Task.Run(ReleaseAsync, TestContext.Current.CancellationToken);
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(owner.IsAvailable);
        Assert.Equal(1, transport.DisposeCount);
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<T> NewCompletion<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CountingTransport : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
            => Interlocked.Increment(ref _disposeCount);
    }
}
