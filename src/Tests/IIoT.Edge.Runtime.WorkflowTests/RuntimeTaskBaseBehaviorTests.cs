using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class RuntimeTaskBaseBehaviorTests
{
    [Fact]
    public async Task PlcTaskBase_WhenCanceled_ShouldStopLoop()
    {
        var task = new CountingPlcTask(firstFailure: null);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await task.SecondExecutionReached.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await runTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.True(runTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PlcTaskBase_WhenDoCoreThrows_ShouldLogAndRetry()
    {
        var task = new CountingPlcTask(
            new InvalidOperationException("planned failure"),
            new InvalidOperationException("planned retry-wait failure"));
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await task.SecondExecutionReached.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await runTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(task.ExecuteCount >= 2);
        Assert.Contains(task.CapturedLogger.Entries, entry => entry.Level == "Error" && entry.Message.Contains("planned failure", StringComparison.Ordinal));
        Assert.Contains(task.CapturedLogger.Entries, entry =>
            entry.Level == "Error" && entry.Message.Contains("planned retry-wait failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlcTaskBase_WhenDoCoreThrowsUnrelatedCancellation_ShouldLogAndRetry()
    {
        using var unrelatedCancellation = new CancellationTokenSource();
        var task = new CountingPlcTask(new OperationCanceledException(unrelatedCancellation.Token));
        using var runtimeCancellation = new CancellationTokenSource();

        var runTask = task.StartAsync(runtimeCancellation.Token);
        await task.SecondExecutionReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        await runtimeCancellation.CancelAsync();
        await runTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.ExecuteCount);
        Assert.Contains(task.CapturedLogger.Entries, entry =>
            entry.Level == "Error" && entry.Message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenCanceled_ShouldStopLoop()
    {
        var task = new CountingScheduledTask(firstFailure: null);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await task.SecondExecutionReached.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        await cts.CancelAsync();
        await runTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        Assert.True(runTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenExecuteThrows_ShouldLogAndRetry()
    {
        var task = new CountingScheduledTask(
            new InvalidOperationException("planned failure"),
            new InvalidOperationException("planned retry-wait failure"));
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await task.SecondExecutionReached.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await runTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(task.ExecuteCount >= 2);
        Assert.Contains(task.CapturedLogger.Entries, entry => entry.Level == "Error" && entry.Message.Contains("planned failure", StringComparison.Ordinal));
        Assert.Contains(task.CapturedLogger.Entries, entry =>
            entry.Level == "Error" && entry.Message.Contains("planned retry-wait failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenExecuteThrowsUnrelatedCancellation_ShouldLogAndRetry()
    {
        using var unrelatedCancellation = new CancellationTokenSource();
        var task = new CountingScheduledTask(new OperationCanceledException(unrelatedCancellation.Token));
        using var runtimeCancellation = new CancellationTokenSource();

        var runTask = task.StartAsync(runtimeCancellation.Token);
        await task.SecondExecutionReached.Task.WaitAsync(TestContext.Current.CancellationToken);
        await runtimeCancellation.CancelAsync();
        await runTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, task.ExecuteCount);
        Assert.Contains(task.CapturedLogger.Entries, entry =>
            entry.Level == "Error" && entry.Message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenCanceled_ShouldAwaitStoppingHookBeforeCompleting()
    {
        var stoppingRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var task = new StoppingScheduledTask(logger, stoppingRelease.Task);
        using var cancellation = new CancellationTokenSource();

        var runtime = task.StartAsync(cancellation.Token);
        await task.ExecutionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await task.StoppingStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.False(runtime.IsCompleted);
        stoppingRelease.TrySetResult();
        await runtime.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已停止", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenStopLogSubscriberThrows_ShouldCompleteSuccessfulStopping()
    {
        var logger = CreateStopLogThrowingLogger();
        var task = new StoppingScheduledTask(logger, Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();

        var runtime = task.StartAsync(cancellation.Token);
        await task.ExecutionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        await runtime.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(runtime.IsCompletedSuccessfully);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已停止", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenStoppingHookAndStopLogSubscriberThrow_ShouldPreserveStoppingFailure()
    {
        var expected = new IOException("durable shutdown evidence failed");
        var logger = CreateStopLogThrowingLogger();
        var task = new StoppingScheduledTask(logger, Task.FromException(expected));
        using var cancellation = new CancellationTokenSource();

        var runtime = task.StartAsync(cancellation.Token);
        await task.ExecutionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var actual = await Assert.ThrowsAsync<IOException>(
            () => runtime.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Same(expected, actual);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已停止", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenExecutionAndStopLogSubscriberThrow_ShouldPreserveExecutionFailure()
    {
        var expected = new InvalidOperationException("critical execution failed");
        var logger = CreateStopLogThrowingLogger();
        var task = new StoppingScheduledTask(
            logger,
            Task.CompletedTask,
            executionFailure: expected,
            propagateExecutionFailure: true);

        var runtime = task.StartAsync(TestContext.Current.CancellationToken);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Same(expected, actual);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已停止", StringComparison.Ordinal));
    }

    private static FakeLogService CreateStopLogThrowingLogger()
    {
        var logger = new FakeLogService();
        logger.EntryAdded += entry =>
        {
            if (entry.Message.Contains("已停止", StringComparison.Ordinal))
                throw new IOException("stop log subscriber failed");
        };
        return logger;
    }

    private sealed class CountingPlcTask : PlcTaskBase
    {
        private readonly Exception? _firstFailure;
        private readonly Exception? _firstRetryWaitFailure;
        private int _executeCount;
        private int _retryWaitCount;

        public CountingPlcTask(Exception? firstFailure, Exception? firstRetryWaitFailure = null)
            : base(new NullPlcBuffer(), new ProductionContext { DeviceName = "PLC-TEST" }, new FakeLogService())
        {
            _firstFailure = firstFailure;
            _firstRetryWaitFailure = firstRetryWaitFailure;
            CapturedLogger = (FakeLogService)Logger;
        }

        public override string TaskName => "CountingPlcTask";

        public FakeLogService CapturedLogger { get; }

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public TaskCompletionSource SecondExecutionReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task WaitForNextIterationAsync(CancellationToken ct)
            => ExecuteCount < 2
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, ct);

        protected override Task WaitForErrorRetryAsync(CancellationToken ct)
            => _firstRetryWaitFailure is not null && Interlocked.Increment(ref _retryWaitCount) == 1
                ? Task.FromException(_firstRetryWaitFailure)
                : Task.CompletedTask;

        protected override Task DoCoreAsync()
        {
            var executeCount = Interlocked.Increment(ref _executeCount);
            if (executeCount >= 2)
            {
                SecondExecutionReached.TrySetResult();
            }

            if (_firstFailure is not null && executeCount == 1)
            {
                throw _firstFailure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CountingScheduledTask : ScheduledTaskBase
    {
        private readonly Exception? _firstFailure;
        private readonly Exception? _firstRetryWaitFailure;
        private int _executeCount;
        private int _retryWaitCount;

        public CountingScheduledTask(Exception? firstFailure, Exception? firstRetryWaitFailure = null)
            : base(new FakeLogService())
        {
            _firstFailure = firstFailure;
            _firstRetryWaitFailure = firstRetryWaitFailure;
            CapturedLogger = (FakeLogService)Logger;
        }

        public override string TaskName => "CountingScheduledTask";

        public FakeLogService CapturedLogger { get; }

        public int ExecuteCount => Volatile.Read(ref _executeCount);

        public TaskCompletionSource SecondExecutionReached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override int ExecuteInterval => 0;

        protected override Task WaitForNextIterationAsync(CancellationToken ct)
            => ExecuteCount < 2
                ? Task.CompletedTask
                : Task.Delay(Timeout.InfiniteTimeSpan, ct);

        protected override Task WaitForErrorRetryAsync(CancellationToken ct)
            => _firstRetryWaitFailure is not null && Interlocked.Increment(ref _retryWaitCount) == 1
                ? Task.FromException(_firstRetryWaitFailure)
                : Task.CompletedTask;

        protected override Task ExecuteAsync()
        {
            var executeCount = Interlocked.Increment(ref _executeCount);
            if (executeCount >= 2)
            {
                SecondExecutionReached.TrySetResult();
            }

            if (_firstFailure is not null && executeCount == 1)
            {
                throw _firstFailure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StoppingScheduledTask : ScheduledTaskBase
    {
        private readonly Task _stoppingTask;
        private readonly Exception? _executionFailure;
        private readonly bool _propagateExecutionFailure;

        public StoppingScheduledTask(
            ILogService logger,
            Task stoppingTask,
            Exception? executionFailure = null,
            bool propagateExecutionFailure = false)
            : base(logger)
        {
            _stoppingTask = stoppingTask;
            _executionFailure = executionFailure;
            _propagateExecutionFailure = propagateExecutionFailure;
        }

        public override string TaskName => "StoppingScheduledTask";

        public TaskCompletionSource ExecutionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StoppingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override int ExecuteInterval => 0;

        protected override Task ExecuteAsync()
        {
            ExecutionStarted.TrySetResult();
            return _executionFailure is null
                ? Task.Delay(Timeout.InfiniteTimeSpan, CurrentCancellationToken)
                : Task.FromException(_executionFailure);
        }

        protected override bool ShouldPropagateExecutionFailure(
            Exception exception,
            CancellationToken cancellationToken)
            => _propagateExecutionFailure;

        protected override async Task OnStoppingAsync()
        {
            StoppingStarted.TrySetResult();
            await _stoppingTask.ConfigureAwait(false);
        }
    }

    private sealed class NullPlcBuffer : IPlcBuffer
    {
        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

        public ushort GetReadValue(int index) => 0;

        public bool TryGetReadWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public bool TryGetWriteWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public void SetWriteValue(int index, ushort value)
        {
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(string.Empty, "Write"));
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, "Write"));
        }
    }
}
