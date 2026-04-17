using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ProcessQueueTaskBehaviorTests
{
    [Fact]
    public async Task DurableConsumerFailure_ShouldPersistRetryRecord()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var failedStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        pipeline.Enqueue(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(logger, pipeline, [cloudConsumer], failedStore, fallbackStore, mesFallbackStore);

        await task.ExecuteOnceAsync();

        Assert.Single(failedStore.PendingRecords);
        Assert.Equal("Cloud", failedStore.PendingRecords[0].Channel);
        Assert.Equal("Cloud", failedStore.PendingRecords[0].FailedTarget);
        Assert.Empty(fallbackStore.Records);
    }

    [Fact]
    public async Task BestEffortFailure_ShouldNotBlockLaterDurableConsumer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var failedStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        pipeline.Enqueue(CreateRecord());

        var uiConsumer = new FakeCellDataConsumer(
            name: "UI",
            order: 10,
            retryChannel: null,
            result: false,
            failureMode: ConsumerFailureMode.BestEffort);

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 20,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(logger, pipeline, [uiConsumer, cloudConsumer], failedStore, fallbackStore, mesFallbackStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, uiConsumer.ProcessCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
        Assert.Single(failedStore.PendingRecords);
        Assert.Equal("Cloud", failedStore.PendingRecords[0].FailedTarget);
        Assert.Contains(logger.Entries, x => x.Message.Contains("(best-effort)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CloudRetryStoreFailure_ShouldWriteToFallbackBuffer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var failedStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("db down")
        };
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        pipeline.Enqueue(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(logger, pipeline, [cloudConsumer], failedStore, fallbackStore, mesFallbackStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, failedStore.SaveCallCount);
        Assert.Single(fallbackStore.Records);
        Assert.Equal("Cloud", fallbackStore.Records[0].FailedTarget);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Cloud fallback buffer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MesRetryStoreFailure_ShouldWriteToMesFallbackBuffer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var failedStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("db down")
        };
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        pipeline.Enqueue(CreateRecord());

        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 10,
            retryChannel: "MES",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(logger, pipeline, [mesConsumer], failedStore, fallbackStore, mesFallbackStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, failedStore.SaveCallCount);
        Assert.Single(mesFallbackStore.Records);
        Assert.Equal("MES", mesFallbackStore.Records[0].FailedTarget);
        Assert.Contains(logger.Entries, x => x.Message.Contains("MES fallback buffer", StringComparison.Ordinal));
    }

    private static CellCompletedRecord CreateRecord()
        => new()
        {
            CellData = new InjectionCellData
            {
                DeviceName = "PLC-A",
                DeviceCode = "PLC-A",
                Barcode = "BC-001",
                WorkOrderNo = "WO-001",
                CompletedTime = new DateTime(2026, 4, 15, 8, 0, 0),
                CellResult = true
            }
        };

    private sealed class TestableProcessQueueTask(
        FakeLogService logger,
        FakeDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        FakeFailedRecordStore failedStore,
        FakeCloudFallbackBufferStore fallbackStore,
        FakeMesFallbackBufferStore mesFallbackStore)
        : ProcessQueueTask(logger, pipelineService, consumers, failedStore, fallbackStore, mesFallbackStore)
    {
        public Task ExecuteOnceAsync() => base.ExecuteAsync();
    }
}
