using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.ContractTests;

public sealed class PeriodicSnapshotUploadTaskBaseContractTests
{
    [Fact]
    public async Task ExecuteOnce_ShouldWaitForUploadReturnBeforeInvokingCallbackExactlyOnce()
    {
        var task = new TestPeriodicSnapshotTask();
        var execution = task.ExecuteOnceAsync(TestContext.Current.CancellationToken);

        await task.UploadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["capture", "upload-start"], task.Events);
        Assert.Equal(0, task.CallbackCount);

        task.AllowUploadToComplete();
        await execution;

        Assert.Equal(["capture", "upload-start", "upload-return", "callback"], task.Events);
        Assert.Equal(1, task.CallbackCount);
    }

    [Fact]
    public async Task ExecuteOnce_WhenUploadIsCancelled_ShouldPropagateCancellationWithoutCallback()
    {
        var task = new TestPeriodicSnapshotTask();
        using var cancellation = new CancellationTokenSource();
        var execution = task.ExecuteOnceAsync(cancellation.Token);

        await task.UploadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(task.UploadCancellationToken.IsCancellationRequested);
        Assert.Equal(0, task.CallbackCount);
        Assert.Equal(["capture", "upload-start"], task.Events);
    }

    [Fact]
    public async Task ExecuteOnce_WhenCallbackIsCancelled_ShouldPropagateCancellationWithoutRepeatingCallback()
    {
        var task = new TestPeriodicSnapshotTask(blockCallback: true);
        using var cancellation = new CancellationTokenSource();
        var execution = task.ExecuteOnceAsync(cancellation.Token);

        await task.UploadStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        task.AllowUploadToComplete();
        await task.CallbackStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(task.CallbackCancellationToken.IsCancellationRequested);
        Assert.Equal(1, task.CallbackCount);
        Assert.Equal(["capture", "upload-start", "upload-return", "callback"], task.Events);
    }

    private sealed class TestPeriodicSnapshotTask : PeriodicSnapshotUploadTaskBase<TestSnapshot>
    {
        private readonly bool _blockCallback;
        private readonly TaskCompletionSource _uploadGate = CreateCompletionSource();
        private readonly TaskCompletionSource _callbackGate = CreateCompletionSource();

        public TestPeriodicSnapshotTask(bool blockCallback = false)
            : base(new TestPlcBuffer(), new ProductionContext { DeviceName = "PLC-TEST-01" }, new TestLogService())
        {
            _blockCallback = blockCallback;
        }

        public override string TaskName => "TestPlugin.PeriodicSnapshot";

        public List<string> Events { get; } = [];

        public TaskCompletionSource UploadStarted { get; } = CreateCompletionSource();

        public TaskCompletionSource CallbackStarted { get; } = CreateCompletionSource();

        public CancellationToken UploadCancellationToken { get; private set; }

        public CancellationToken CallbackCancellationToken { get; private set; }

        public int CallbackCount { get; private set; }

        public Task ExecuteOnceAsync(CancellationToken cancellationToken)
        {
            SetTaskCancellationToken(cancellationToken);
            return DoCoreAsync();
        }

        public void AllowUploadToComplete()
            => _uploadGate.TrySetResult();

        protected override TestSnapshot CaptureSnapshot()
        {
            Events.Add("capture");
            return new TestSnapshot();
        }

        protected override async Task<MesCallResult> UploadSnapshotAsync(
            TestSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            UploadCancellationToken = cancellationToken;
            Events.Add("upload-start");
            UploadStarted.TrySetResult();
            await _uploadGate.Task.WaitAsync(cancellationToken);
            Events.Add("upload-return");
            return MesCallResult.Success();
        }

        protected override async Task OnSnapshotUploadedAsync(
            TestSnapshot snapshot,
            MesCallResult result,
            CancellationToken cancellationToken)
        {
            CallbackCancellationToken = cancellationToken;
            CallbackCount++;
            Events.Add("callback");
            CallbackStarted.TrySetResult();
            if (_blockCallback)
            {
                await _callbackGate.Task.WaitAsync(cancellationToken);
            }
        }

        private static TaskCompletionSource CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record TestSnapshot;

    private sealed class TestPlcBuffer : IPlcBuffer
    {
        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged
        {
            add { }
            remove { }
        }

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
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
        }
    }

    private sealed class TestLogService : ILogService
    {
        public event Action<LogEntry>? EntryAdded
        {
            add { }
            remove { }
        }

        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Fatal(string message) { }
    }
}
