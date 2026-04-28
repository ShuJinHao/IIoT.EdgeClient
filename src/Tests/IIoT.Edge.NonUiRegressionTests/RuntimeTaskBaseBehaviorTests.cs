using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class RuntimeTaskBaseBehaviorTests
{
    [Fact]
    public async Task PlcTaskBase_WhenCanceled_ShouldStopLoop()
    {
        var task = new CountingPlcTask(throwFirst: false);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await WaitUntilAsync(() => task.ExecuteCount >= 2);

        await cts.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        var countAfterCancel = task.ExecuteCount;

        await AssertCountRemainsAsync(() => task.ExecuteCount, countAfterCancel, TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task PlcTaskBase_WhenDoCoreThrows_ShouldLogAndRetry()
    {
        var task = new CountingPlcTask(throwFirst: true);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await WaitUntilAsync(() => task.ExecuteCount >= 2);
        await cts.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(task.ExecuteCount >= 2);
        Assert.Contains(task.CapturedLogger.Entries, entry => entry.Level == "Error" && entry.Message.Contains("planned failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenCanceled_ShouldStopLoop()
    {
        var task = new CountingScheduledTask(throwFirst: false);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await WaitUntilAsync(() => task.ExecuteCount >= 2);

        await cts.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));
        var countAfterCancel = task.ExecuteCount;

        await AssertCountRemainsAsync(() => task.ExecuteCount, countAfterCancel, TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task ScheduledTaskBase_WhenExecuteThrows_ShouldLogAndRetry()
    {
        var task = new CountingScheduledTask(throwFirst: true);
        using var cts = new CancellationTokenSource();

        var runTask = task.StartAsync(cts.Token);
        await WaitUntilAsync(() => task.ExecuteCount >= 2);
        await cts.CancelAsync();
        await runTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(task.ExecuteCount >= 2);
        Assert.Contains(task.CapturedLogger.Entries, entry => entry.Level == "Error" && entry.Message.Contains("planned failure", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.True(condition(), "Condition was not satisfied before timeout.");
    }

    private static async Task AssertCountRemainsAsync(
        Func<int> getCount,
        int expected,
        TimeSpan duration)
    {
        var deadline = DateTime.UtcNow.Add(duration);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, getCount());
            await Task.Yield();
        }

        Assert.Equal(expected, getCount());
    }

    private sealed class CountingPlcTask : PlcTaskBase
    {
        private readonly bool _throwFirst;

        public CountingPlcTask(bool throwFirst)
            : base(new NullPlcBuffer(), new ProductionContext { DeviceName = "PLC-TEST" }, new FakeLogService())
        {
            _throwFirst = throwFirst;
            CapturedLogger = (FakeLogService)Logger;
        }

        public override string TaskName => "CountingPlcTask";

        public FakeLogService CapturedLogger { get; }

        public int ExecuteCount { get; private set; }

        protected override int TaskLoopInterval => 1;

        protected override int ErrorRetryInterval => 1;

        protected override Task DoCoreAsync()
        {
            ExecuteCount++;
            if (_throwFirst && ExecuteCount == 1)
            {
                throw new InvalidOperationException("planned failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CountingScheduledTask : ScheduledTaskBase
    {
        private readonly bool _throwFirst;

        public CountingScheduledTask(bool throwFirst)
            : base(new FakeLogService())
        {
            _throwFirst = throwFirst;
            CapturedLogger = (FakeLogService)Logger;
        }

        public override string TaskName => "CountingScheduledTask";

        public FakeLogService CapturedLogger { get; }

        public int ExecuteCount { get; private set; }

        protected override int ExecuteInterval => 1;

        protected override int ErrorRetryInterval => 1;

        protected override Task ExecuteAsync()
        {
            ExecuteCount++;
            if (_throwFirst && ExecuteCount == 1)
            {
                throw new InvalidOperationException("planned failure");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NullPlcBuffer : IPlcBuffer
    {
        public ushort GetReadValue(int index) => 0;

        public void SetWriteValue(int index, ushort value)
        {
        }
    }
}
