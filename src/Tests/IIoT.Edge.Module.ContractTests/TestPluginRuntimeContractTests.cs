using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.TestPlugin;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.ContractTests;

public sealed class TestPluginRuntimeContractTests
{
    [Fact]
    public void RuntimeFactory_ShouldExposeAndCreateNeutralSnapshotTask()
    {
        var pipeline = new GatedDataPipelineService();
        var services = new ServiceCollection()
            .AddSingleton<IDataPipelineService>(pipeline)
            .AddSingleton<ILogService, TestLogService>()
            .BuildServiceProvider();
        var factory = new TestPluginRuntimeFactory();

        var candidate = Assert.Single(factory.GetTaskCandidates());
        Assert.Equal(TestPluginRuntimeFactory.SnapshotTaskKey, candidate.Key);
        Assert.True(candidate.DefaultEnabled);
        Assert.Empty(factory.CreateTasks(
            services,
            new TestPlcBuffer(),
            CreateContext(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        var task = Assert.IsType<TestPluginSnapshotTask>(Assert.Single(factory.CreateTasks(
            services,
            new TestPlcBuffer(),
            CreateContext(),
            new HashSet<string>([candidate.Key], StringComparer.OrdinalIgnoreCase))));
        Assert.Equal(candidate.Key, task.TaskName);
    }

    [Fact]
    public async Task ExecuteOnce_ShouldInvokeCallbackOnlyAfterPipelineAcceptanceReturns()
    {
        var pipeline = new GatedDataPipelineService();
        var task = new TestPluginSnapshotTask(
            new TestPlcBuffer(),
            CreateContext(),
            pipeline,
            new TestLogService());

        var execution = task.ExecuteOnceAsync(TestContext.Current.CancellationToken);
        await pipeline.EnqueueStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, task.CaptureCount);
        Assert.Equal(1, pipeline.EnqueueCallCount);
        Assert.Equal(0, task.CallbackCount);
        Assert.Null(task.LastEnqueueResult);
        var record = Assert.IsType<CellCompletedRecord>(pipeline.LastRecord);
        Assert.Equal(DependencyInjection.ModuleKey, record.ModuleId);
        Assert.Equal(TestPluginRuntimeFactory.SnapshotTaskKey, record.TaskKey);
        Assert.Equal(17, record.NetworkDeviceId);
        Assert.Equal("PLC-TEST-01", record.DeviceName);

        pipeline.AllowEnqueueToReturn();
        await execution;

        Assert.True(task.LastEnqueueResult?.IsDurablyAccepted);
        Assert.True(task.LastCallbackResult?.IsSuccess);
        Assert.Equal(1, task.CaptureCount);
        Assert.Equal(1, pipeline.EnqueueCallCount);
        Assert.Equal(1, task.CallbackCount);
    }

    [Fact]
    public async Task ExecuteOnce_WhenCallerCancelsPendingEnqueue_ShouldPropagateWithoutCallback()
    {
        var pipeline = new GatedDataPipelineService();
        var task = new TestPluginSnapshotTask(
            new TestPlcBuffer(),
            CreateContext(),
            pipeline,
            new TestLogService());
        using var cancellation = new CancellationTokenSource();

        var execution = task.ExecuteOnceAsync(cancellation.Token);
        await pipeline.EnqueueStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.True(pipeline.EnqueueCancellationToken.IsCancellationRequested);
        Assert.Equal(1, task.CaptureCount);
        Assert.Equal(1, pipeline.EnqueueCallCount);
        Assert.Equal(0, task.CallbackCount);
        Assert.Null(task.LastEnqueueResult);
    }

    private static ProductionContext CreateContext()
        => new()
        {
            NetworkDeviceId = 17,
            DeviceName = "PLC-TEST-01"
        };

    private sealed class GatedDataPipelineService : IDataPipelineService
    {
        private readonly TaskCompletionSource _allowEnqueueReturn =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _enqueueCallCount;

        public TaskCompletionSource EnqueueStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int EnqueueCallCount => Volatile.Read(ref _enqueueCallCount);

        public CellCompletedRecord? LastRecord { get; private set; }

        public CancellationToken EnqueueCancellationToken { get; private set; }

        public int PendingCount => 0;

        public int OverflowCount => 0;

        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
            => EnqueueCoreAsync(record, cancellationToken);

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            record = null;
            return false;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(false);

        public void AllowEnqueueToReturn()
            => _allowEnqueueReturn.TrySetResult();

        private async ValueTask<DataPipelineEnqueueResult> EnqueueCoreAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _enqueueCallCount);
            LastRecord = record;
            EnqueueCancellationToken = cancellationToken;
            EnqueueStarted.TrySetResult();
            await _allowEnqueueReturn.Task.WaitAsync(cancellationToken);
            return DataPipelineEnqueueResult.Accepted();
        }
    }

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
