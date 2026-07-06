using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
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

        var retry = Assert.Single(cloudRetryStore.PendingRecords);
        Assert.Equal("Cloud", retry.Channel);
        Assert.Equal("Cloud", retry.FailedTarget);
        AssertStoredContext(retry);
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
        Assert.Contains(logger.Entries, x => x.Message.Contains("非关键消费者", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessOneAsync_WhenRecordTargetsCloudOnly_ShouldNotCallMesConsumer()
    {
        var pipeline = new FakeDataPipelineService();
        var record = CreateRecord();
        record.CellData.UploadTargets = DataPipelineUploadTargets.Cloud;
        await pipeline.EnqueueAsync(record);

        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable);
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [mesConsumer, cloudConsumer],
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter());

        await task.ExecuteOnceAsync();

        Assert.Equal(0, mesConsumer.ProcessCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task ProcessOneAsync_WhenMesIsBlocked_ShouldStillStartCloudConsumer()
    {
        var pipeline = new FakeDataPipelineService();
        var record = CreateRecord();
        record.CellData.UploadTargets = DataPipelineUploadTargets.All;
        await pipeline.EnqueueAsync(record);

        var mesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async _ =>
            {
                mesStarted.SetResult();
                await releaseMes.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: _ =>
            {
                cloudCompleted.SetResult();
                return Task.FromResult(true);
            });

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [mesConsumer, cloudConsumer],
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter());

        var executeTask = task.ExecuteOnceAsync();
        await mesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cloudCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(executeTask.IsCompleted);

        releaseMes.SetResult();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task ExecuteOnceAsync_WhenPreviousMesRecordIsBlocked_ShouldStillStartLaterCloudOnlyRecord()
    {
        var pipeline = new FakeDataPipelineService();
        var mesOnlyRecord = CreateRecord();
        ((TestProcessCellData)mesOnlyRecord.CellData).Barcode = "MES-BLOCKED";
        mesOnlyRecord.CellData.UploadTargets = DataPipelineUploadTargets.Mes;
        var cloudOnlyRecord = CreateRecord();
        ((TestProcessCellData)cloudOnlyRecord.CellData).Barcode = "CLOUD-FOLLOWS";
        cloudOnlyRecord.CellData.UploadTargets = DataPipelineUploadTargets.Cloud;
        await pipeline.EnqueueAsync(mesOnlyRecord);
        await pipeline.EnqueueAsync(cloudOnlyRecord);

        var mesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async _ =>
            {
                mesStarted.SetResult();
                await releaseMes.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: _ =>
            {
                cloudCompleted.SetResult();
                return Task.FromResult(true);
            });

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [mesConsumer, cloudConsumer],
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter());

        var executeTask = task.ExecuteOnceAsync();
        await mesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cloudCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(executeTask.IsCompleted);

        releaseMes.SetResult();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task ExecuteOnceAsync_WhenMesOutletQueueIsFull_ShouldPersistMesRetryWithoutBlockingCloud()
    {
        var pipeline = new FakeDataPipelineService();
        for (var i = 0; i < 99; i++)
        {
            var mesRecord = CreateRecord();
            ((TestProcessCellData)mesRecord.CellData).Barcode = $"MES-QUEUE-{i:D2}";
            mesRecord.CellData.UploadTargets = DataPipelineUploadTargets.Mes;
            await pipeline.EnqueueAsync(mesRecord, TestContext.Current.CancellationToken);
        }

        var cloudOnlyRecord = CreateRecord();
        ((TestProcessCellData)cloudOnlyRecord.CellData).Barcode = "CLOUD-AFTER-MES-FULL";
        cloudOnlyRecord.CellData.UploadTargets = DataPipelineUploadTargets.Cloud;
        await pipeline.EnqueueAsync(cloudOnlyRecord, TestContext.Current.CancellationToken);

        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var mesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async _ =>
            {
                mesStarted.TrySetResult();
                await releaseMes.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken);
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: _ =>
            {
                cloudCompleted.SetResult();
                return Task.FromResult(true);
            });

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [mesConsumer, cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            runtimeOptions: new DataPipelineRuntimeOptions
            {
                DurableOutletQueueCapacity = 1
            });

        var executeTask = task.ExecuteOnceAsync();
        await mesStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await cloudCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => mesRetryStore.PendingRecords.Count > 0,
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(executeTask.IsCompleted);
        Assert.NotEmpty(mesRetryStore.PendingRecords);
        Assert.All(mesRetryStore.PendingRecords, retry =>
        {
            Assert.Equal("MES", retry.Channel);
            Assert.Equal("MES", retry.FailedTarget);
            Assert.Equal("目标出口队列已满。", retry.ErrorMessage);
            AssertStoredContext(retry);
        });
        Assert.Empty(cloudRetryStore.PendingRecords);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);

        releaseMes.SetResult();
        await executeTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(mesConsumer.ProcessCallCount > 0);
        Assert.Equal(0, pipeline.PendingCount);
    }

    [Fact]
    public async Task DurableConsumerTimeout_ShouldPersistRetryRecordWithTimeoutReason()
    {
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        await pipeline.EnqueueAsync(CreateRecord());

        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: _ => Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => true));

        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            runtimeOptions: new DataPipelineRuntimeOptions
            {
                ConsumerCallTimeoutSeconds = 1
            });

        await task.ExecuteOnceAsync();

        var retry = Assert.Single(cloudRetryStore.PendingRecords);
        Assert.Equal("处理超时。", retry.ErrorMessage);
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
        var fallback = Assert.Single(fallbackStore.Records);
        Assert.Equal("Cloud", fallback.FailedTarget);
        AssertStoredContext(fallback);
        Assert.Contains(logger.Entries, x => x.Message.Contains("云端 兜底缓存", StringComparison.Ordinal));
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
        AssertStoredContext(deadLetter);
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
            ProcessType = TestProcessCellData.ProcessTypeKey,
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
        Assert.Equal("容量受限:补传:total", deadLetter.FailureReason);
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
            ProcessType = TestProcessCellData.ProcessTypeKey,
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
        var testProcessPipeline = new FakeDataPipelineService();
        await testProcessPipeline.EnqueueAsync(CreateTestProcessRecord());

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

        var testProcessTask = new TestableProcessQueueTask(
            logger,
            testProcessPipeline,
            [consumer],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            cloudDeadLetterStore,
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter(),
            capacityGuard);

        await testProcessTask.ExecuteOnceAsync();

        Assert.Contains(cloudDeadLetterStore.Records, x => x.FailureReason == "容量受限:补传:process_type");
        Assert.Equal(2, cloudRetryStore.PendingRecords.Count);
        Assert.Contains(cloudRetryStore.PendingRecords, x => x.ProcessType == "OtherProcess");
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
            ProcessType = TestProcessCellData.ProcessTypeKey,
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
        Assert.Equal("容量受限:兜底:total", deadLetter.FailureReason);
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
        var fallback = Assert.Single(mesFallbackStore.Records);
        Assert.Equal("MES", fallback.FailedTarget);
        AssertStoredContext(fallback);
        Assert.Contains(logger.Entries, x => x.Message.Contains("MES 兜底缓存", StringComparison.Ordinal));
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
            NetworkDeviceId = 1001,
            DeviceName = "PLC-A",
            ModuleId = "DieCuttingCathode",
            TaskKey = "DieCuttingCathode.Realtime",
            PlanSessionId = "SESSION-001",
            MainPlanCode = "PLAN-001",
            TraceBatchNumber = "TRACE-001",
            CellData = new TestProcessCellData
            {
                PlcDeviceId = 1001,
                DeviceName = "PLC-A",
                DeviceCode = "PLC-A",
                Barcode = "BC-001",
                WorkOrderNo = "WO-001",
                CompletedTime = new DateTime(2026, 4, 15, 8, 0, 0),
                CellResult = true
            }
        };

    private static void AssertStoredContext(FailedCellRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("DieCuttingCathode", record.ModuleId);
        Assert.Equal("DieCuttingCathode.Realtime", record.TaskKey);
        Assert.Equal("SESSION-001", record.PlanSessionId);
        Assert.Equal("PLAN-001", record.MainPlanCode);
        Assert.Equal("TRACE-001", record.TraceBatchNumber);
    }

    private static void AssertStoredContext(IFallbackRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("DieCuttingCathode", record.ModuleId);
        Assert.Equal("DieCuttingCathode.Realtime", record.TaskKey);
        Assert.Equal("SESSION-001", record.PlanSessionId);
        Assert.Equal("PLAN-001", record.MainPlanCode);
        Assert.Equal("TRACE-001", record.TraceBatchNumber);
    }

    private static void AssertStoredContext(DeadLetterRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("DieCuttingCathode", record.ModuleId);
        Assert.Equal("DieCuttingCathode.Realtime", record.TaskKey);
        Assert.Equal("SESSION-001", record.PlanSessionId);
        Assert.Equal("PLAN-001", record.MainPlanCode);
        Assert.Equal("TRACE-001", record.TraceBatchNumber);
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken);
        while (!condition())
        {
            await Task.Delay(10, linkedCts.Token);
        }
    }

    private static CellCompletedRecord CreateTestProcessRecord()
        => new()
        {
            CellData = new TestCellData
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
        DataPipelineCapacityGuard? capacityGuard = null,
        DataPipelineRuntimeOptions? runtimeOptions = null)
        : ProcessQueueTask(
            logger,
            pipelineService,
            consumers,
            criticalWriter,
            CreatePersistenceWriter(
                logger,
                cloudRetryStore,
                mesRetryStore,
                fallbackStore,
                mesFallbackStore,
                cloudDeadLetterStore,
                mesDeadLetterStore,
                criticalWriter,
                capacityGuard),
            new DefaultDataPipelineConsumerInvoker(),
            runtimeOptions)
    {
        public async Task ExecuteOnceAsync()
        {
            await base.ExecuteAsync();
            await WaitForDurableQueuesIdleAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static DataPipelineCascadingPersistenceWriter CreatePersistenceWriter(
        FakeLogService logger,
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudFallbackBufferStore fallbackStore,
        FakeMesFallbackBufferStore mesFallbackStore,
        FakeCloudDeadLetterStore cloudDeadLetterStore,
        FakeMesDeadLetterStore mesDeadLetterStore,
        FakeCriticalPersistenceFallbackWriter criticalWriter,
        DataPipelineCapacityGuard? capacityGuard)
        => new(
            cloudRetryStore,
            mesRetryStore,
            fallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter,
            capacityGuard ?? CreateCapacityGuard(
                logger,
                cloudRetryStore,
                mesRetryStore,
                fallbackStore,
                mesFallbackStore,
                new FakeCloudDiagnosticsStore(),
                new FakeMesRetryDiagnosticsStore()),
            logger,
            new CellDataJsonSerializer(new CellDataTypeRegistry()));
    }
