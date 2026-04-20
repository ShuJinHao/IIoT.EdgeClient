using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ProcessQueueTaskBehaviorTests
{
    [Fact]
    public async Task DurableConsumerFailure_ShouldPersistRetryRecord()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Single(cloudRetryStore.PendingRecords);
        Assert.Equal("Cloud", cloudRetryStore.PendingRecords[0].Channel);
        Assert.Equal("Cloud", cloudRetryStore.PendingRecords[0].FailedTarget);
        Assert.Empty(mesRetryStore.PendingRecords);
        Assert.Empty(fallbackStore.Records);
    }

    [Fact]
    public async Task BestEffortFailure_ShouldNotBlockLaterDurableConsumer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());

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

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [uiConsumer, cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, uiConsumer.ProcessCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
        Assert.Single(cloudRetryStore.PendingRecords);
        Assert.Equal("Cloud", cloudRetryStore.PendingRecords[0].FailedTarget);
        Assert.Empty(mesRetryStore.PendingRecords);
        Assert.Contains(logger.Entries, x => x.Message.Contains("(best-effort)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CloudRetryStoreFailure_ShouldWriteToFallbackBuffer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("db down")
        };
        var mesRetryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudRetryStore.SaveCallCount);
        Assert.Single(fallbackStore.Records);
        Assert.Equal("Cloud", fallbackStore.Records[0].FailedTarget);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Cloud fallback buffer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CloudRetryAndFallbackFailure_ShouldPersistDeadLetter()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("retry down")
        };
        var mesRetryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore
        {
            SaveException = new InvalidOperationException("fallback down")
        };
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        var deadLetter = Assert.Single(cloudDeadLetterStore.Records);
        Assert.Equal("Cloud", deadLetter.FailedTarget);
        Assert.Equal(nameof(DeadLetterStage.FallbackPersist), deadLetter.FailureStage);
        Assert.Empty(criticalWriter.Writes);
    }

    [Fact]
    public async Task DurableConsumerFailure_WhenCloudRetryCapacityIsBlocked_ShouldPersistDeadLetter()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "Injection",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var diagnosticsStore = new FakeCloudDiagnosticsStore();
        var capacityGuard = CreateCapacityGuard(
            logger,
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            diagnosticsStore,
            new FakeMesRetryDiagnosticsStore(),
            configure: options => options.Cloud.RetryTotalLimit = 1);

        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        await pipeline.EnqueueAsync(CreateRecord());

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [
                new FakeCellDataConsumer(
                    name: "Cloud",
                    order: 10,
                    retryChannel: "Cloud",
                    result: false,
                    failureMode: ConsumerFailureMode.Durable)
            ],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            cloudDeadLetterStore,
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            capacityGuard);

        await task.ExecuteOnceAsync();

        var deadLetter = Assert.Single(cloudDeadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.CapacityBlocked), deadLetter.FailureStage);
        Assert.Equal("capacity_blocked:retry:total", deadLetter.FailureReason);
        Assert.Single(cloudRetryStore.PendingRecords);
        Assert.True(diagnosticsStore.Snapshot.IsCapacityBlocked);
        Assert.Equal(CapacityBlockedChannel.Retry, diagnosticsStore.Snapshot.BlockedChannel);
    }

    [Fact]
    public async Task DurableConsumerFailure_WhenCloudRetryProcessTypeCapacityIsBlocked_ShouldStillAllowOtherProcessTypes()
    {
        var logger = new FakeLogService();
        var cloudRetryStore = new FakeFailedRecordStore();
        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "Injection",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var capacityGuard = CreateCapacityGuard(
            logger,
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            configure: options =>
            {
                options.Cloud.RetryTotalLimit = 10;
                options.Cloud.RetryPerProcessTypeLimit = 1;
            });

        var injectionPipeline = new FakeDataPipelineService();
        await injectionPipeline.EnqueueAsync(CreateRecord());
        var stackingPipeline = new FakeDataPipelineService();
        await stackingPipeline.EnqueueAsync(CreateStackingRecord());

        var consumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var injectionTask = new TestableProcessQueueTask(
            logger,
            injectionPipeline,
            [consumer],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            cloudDeadLetterStore,
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            capacityGuard);

        await injectionTask.ExecuteOnceAsync();

        var stackingTask = new TestableProcessQueueTask(
            logger,
            stackingPipeline,
            [consumer],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            cloudDeadLetterStore,
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            capacityGuard);

        await stackingTask.ExecuteOnceAsync();

        Assert.Contains(cloudDeadLetterStore.Records, x => x.FailureReason == "capacity_blocked:retry:process_type");
        Assert.Equal(2, cloudRetryStore.PendingRecords.Count);
        Assert.Contains(cloudRetryStore.PendingRecords, x => x.ProcessType == "Stacking");
    }

    [Fact]
    public async Task CloudRetryStoreFailure_WhenCloudFallbackCapacityIsBlocked_ShouldPersistDeadLetter()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("db down")
        };
        var fallbackStore = new FakeCloudFallbackBufferStore();
        fallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 1,
            ProcessType = "Injection",
            CellDataJson = "{}",
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow
        });

        var capacityGuard = CreateCapacityGuard(
            logger,
            cloudRetryStore,
            new FakeFailedRecordStore(),
            fallbackStore,
            new FakeMesFallbackBufferStore(),
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            configure: options => options.Cloud.FallbackTotalLimit = 1);

        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        await pipeline.EnqueueAsync(CreateRecord());

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [
                new FakeCellDataConsumer(
                    name: "Cloud",
                    order: 10,
                    retryChannel: "Cloud",
                    result: false,
                    failureMode: ConsumerFailureMode.Durable)
            ],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            fallbackStore,
            new FakeMesFallbackBufferStore(),
            cloudDeadLetterStore,
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            capacityGuard);

        await task.ExecuteOnceAsync();

        var deadLetter = Assert.Single(cloudDeadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.CapacityBlocked), deadLetter.FailureStage);
        Assert.Equal("capacity_blocked:fallback:total", deadLetter.FailureReason);
        Assert.Single(fallbackStore.Records);
    }

    [Fact]
    public async Task MesRetryStoreFailure_ShouldWriteToMesFallbackBuffer()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("db down")
        };
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());

        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 10,
            retryChannel: "MES",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [mesConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, mesRetryStore.SaveCallCount);
        Assert.Single(mesFallbackStore.Records);
        Assert.Equal("MES", mesFallbackStore.Records[0].FailedTarget);
        Assert.Contains(logger.Entries, x => x.Message.Contains("MES fallback buffer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteOnceAsync_ShouldDrainMultipleQueuedRecords()
    {
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        await pipeline.EnqueueAsync(CreateRecord());
        await pipeline.EnqueueAsync(CreateRecord());
        await pipeline.EnqueueAsync(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Equal(3, cloudConsumer.ProcessCallCount);
        Assert.Equal(0, pipeline.PendingCount);
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

    private static CellCompletedRecord CreateStackingRecord()
        => new()
        {
            CellData = new StackingCellData
            {
                DeviceName = "PLC-B",
                DeviceCode = "PLC-B",
                Barcode = "ST-001",
                TrayCode = "TRAY-002",
                LayerCount = 4,
                SequenceNo = 2,
                RuntimeStatus = "Completed",
                CompletedTime = new DateTime(2026, 4, 15, 8, 5, 0),
                CellResult = true
            }
        };

    private static DataPipelineCapacityGuard CreateCapacityGuard(
        FakeLogService logger,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        FakeCloudDiagnosticsStore cloudDiagnosticsStore,
        FakeMesRetryDiagnosticsStore mesDiagnosticsStore,
        Action<DataPipelineCapacityOptions>? configure = null)
    {
        var options = new DataPipelineCapacityOptions();
        configure?.Invoke(options);
        return new DataPipelineCapacityGuard(
            Options.Create(options),
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDiagnosticsStore,
            mesDiagnosticsStore,
            logger);
    }

    private sealed class TestableProcessQueueTask(
        FakeLogService logger,
        FakeDataPipelineService pipelineService,
        IEnumerable<ICellDataConsumer> consumers,
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudFallbackBufferStore fallbackStore,
        FakeMesFallbackBufferStore mesFallbackStore,
        FakeCloudDeadLetterStore cloudDeadLetterStore,
        FakeMesDeadLetterStore mesDeadLetterStore,
        FakeCriticalPersistenceFallbackWriter criticalWriter,
        DataPipelineCapacityGuard? capacityGuard = null)
        : ProcessQueueTask(
            logger,
            pipelineService,
            consumers,
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter,
            capacityGuard)
    {
        public Task ExecuteOnceAsync() => base.ExecuteAsync();
    }
}
