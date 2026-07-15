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
