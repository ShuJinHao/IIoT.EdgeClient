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
    public async Task TestPluginRecord_WhenRealPipelineAccepts_ShouldReachDurableConsumerExactlyOnce()
    {
        var events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowConsumerToComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                events.Enqueue("durable-start");
                consumerStarted.TrySetResult();
                await allowConsumerToComplete.Task.WaitAsync(cancellationToken);
                events.Enqueue("durable-complete");
                consumerCompleted.TrySetResult();
                return true;
            });
        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        var enqueueResult = await pipeline.EnqueueAsync(
            CreateTestPluginRecord(),
            TestContext.Current.CancellationToken);
        events.Enqueue("enqueue-accepted");
        using var cancellation = new CancellationTokenSource();
        Task? runtime = null;

        try
        {
            Assert.True(enqueueResult.IsDurablyAccepted);
            Assert.Equal(1, pipeline.PendingCount);
            Assert.Equal(0, cloudConsumer.ProcessCallCount);

            runtime = task.StartAsync(cancellation.Token);
            await consumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["enqueue-accepted", "durable-start"], events.ToArray());
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            Assert.Equal(0, pipeline.PendingCount);
            Assert.False(consumerCompleted.Task.IsCompleted);

            allowConsumerToComplete.TrySetResult();
            await consumerCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                ["enqueue-accepted", "durable-start", "durable-complete"],
                events.ToArray());
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            AssertNoCompensationWrites(
                cloudRetryStore,
                mesRetryStore,
                cloudFallbackStore,
                mesFallbackStore,
                cloudDeadLetterStore,
                mesDeadLetterStore,
                criticalWriter);
        }
        finally
        {
            allowConsumerToComplete.TrySetResult();
            await cancellation.CancelAsync();
            if (runtime is not null)
            {
                await runtime.WaitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task TestPluginRecords_WhenRuntimeIsCancelledWithQueuedDurableItem_ShouldDrainWithoutCompensation()
    {
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var observedPipeline = new ObservedDataPipelineService(pipeline);
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                consumerStarted.TrySetResult();
                try
                {
                    await neverComplete.Task.WaitAsync(cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    consumerCancelled.TrySetResult();
                    throw;
                }
            });
        var task = new TestableProcessQueueTask(
            logger,
            observedPipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);
        var firstEnqueueResult = await pipeline.EnqueueAsync(
            CreateTestPluginRecord(),
            TestContext.Current.CancellationToken);
        var queuedEnqueueResult = await pipeline.EnqueueAsync(
            CreateTestPluginRecord(),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();

        var runtime = task.StartAsync(cancellation.Token);
        try
        {
            await consumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.SecondRecordDequeued.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.CurrentDrainCompleted.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, pipeline.PendingCount);
            await cancellation.CancelAsync();

            await consumerCancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True(firstEnqueueResult.IsDurablyAccepted);
            Assert.True(queuedEnqueueResult.IsDurablyAccepted);
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            Assert.Equal(0, pipeline.PendingCount);
            AssertNoCompensationWrites(
                cloudRetryStore,
                mesRetryStore,
                cloudFallbackStore,
                mesFallbackStore,
                cloudDeadLetterStore,
                mesDeadLetterStore,
                criticalWriter);
        }
        finally
        {
            await cancellation.CancelAsync();
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TestPluginRecord_WhenDurableConsumerSelfCancels_ShouldPersistExactlyOneRetryRecord()
    {
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) =>
            {
                consumerStarted.TrySetResult();
                throw new OperationCanceledException("provider cancelled itself");
            });
        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);
        var enqueueResult = await pipeline.EnqueueAsync(
            CreateTestPluginRecord(),
            TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var runtime = task.StartAsync(cancellation.Token);

        try
        {
            await consumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));

            Assert.True(enqueueResult.IsDurablyAccepted);
            Assert.False(cancellation.IsCancellationRequested);
            Assert.False(runtime.IsCompleted);
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            var retry = Assert.Single(cloudRetryStore.PendingRecords);
            Assert.Equal("Cloud", retry.Channel);
            Assert.Equal("Cloud", retry.FailedTarget);
            Assert.Contains("provider cancelled itself", retry.ErrorMessage, StringComparison.Ordinal);
            Assert.Empty(mesRetryStore.PendingRecords);
            Assert.Empty(cloudFallbackStore.Records);
            Assert.Empty(mesFallbackStore.Records);
            Assert.Empty(cloudDeadLetterStore.Records);
            Assert.Empty(mesDeadLetterStore.Records);
            Assert.Empty(criticalWriter.Writes);
        }
        finally
        {
            await cancellation.CancelAsync();
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);

        var mesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                mesStarted.SetResult();
                await releaseMes.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) =>
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
        await mesStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cloudCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(executeTask.IsCompleted);

        releaseMes.SetResult();
        await executeTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
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
        await pipeline.EnqueueAsync(mesOnlyRecord, TestContext.Current.CancellationToken);
        await pipeline.EnqueueAsync(cloudOnlyRecord, TestContext.Current.CancellationToken);

        var mesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                mesStarted.SetResult();
                await releaseMes.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) =>
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
        await mesStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cloudCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.False(executeTask.IsCompleted);

        releaseMes.SetResult();
        await executeTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
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
            processAsync: async (_, cancellationToken) =>
            {
                mesStarted.TrySetResult();
                await releaseMes.Task.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
                return true;
            });
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 25,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) =>
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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

        // Intentionally ignore the consumer token: this case verifies the queue's own per-call timeout.
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) => Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => true));

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await injectionPipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);
        var testProcessPipeline = new FakeDataPipelineService();
        await testProcessPipeline.EnqueueAsync(
            CreateTestProcessRecord(),
            TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

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
            ModuleId = "TestPluginBeta",
            TaskKey = "TestPluginBeta.Realtime",
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

    private static CellCompletedRecord CreateTestPluginRecord()
        => new()
        {
            NetworkDeviceId = 17,
            DeviceName = "PLC-TEST-01",
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.Snapshot",
            CreatedAtUtc = DateTime.UtcNow,
            CellData = new TestPluginWorkflowCellData
            {
                PlcDeviceId = 17,
                DeviceName = "PLC-TEST-01",
                DeviceCode = "PLC-TEST-01",
                CompletedTime = DateTime.UtcNow,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

    private static void AssertNoCompensationWrites(
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudFallbackBufferStore cloudFallbackStore,
        FakeMesFallbackBufferStore mesFallbackStore,
        FakeCloudDeadLetterStore cloudDeadLetterStore,
        FakeMesDeadLetterStore mesDeadLetterStore,
        FakeCriticalPersistenceFallbackWriter criticalWriter)
    {
        Assert.Empty(cloudRetryStore.PendingRecords);
        Assert.Empty(mesRetryStore.PendingRecords);
        Assert.Empty(cloudFallbackStore.Records);
        Assert.Empty(mesFallbackStore.Records);
        Assert.Empty(cloudDeadLetterStore.Records);
        Assert.Empty(mesDeadLetterStore.Records);
        Assert.Empty(criticalWriter.Writes);
    }

    private static void AssertStoredContext(FailedCellRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("TestPluginBeta", record.ModuleId);
        Assert.Equal("TestPluginBeta.Realtime", record.TaskKey);
        Assert.Equal("SESSION-001", record.PlanSessionId);
        Assert.Equal("PLAN-001", record.MainPlanCode);
        Assert.Equal("TRACE-001", record.TraceBatchNumber);
    }

    private static void AssertStoredContext(IFallbackRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("TestPluginBeta", record.ModuleId);
        Assert.Equal("TestPluginBeta.Realtime", record.TaskKey);
        Assert.Equal("SESSION-001", record.PlanSessionId);
        Assert.Equal("PLAN-001", record.MainPlanCode);
        Assert.Equal("TRACE-001", record.TraceBatchNumber);
    }

    private static void AssertStoredContext(DeadLetterRecord record)
    {
        Assert.Equal(1001, record.NetworkDeviceId);
        Assert.Equal("PLC-A", record.DeviceName);
        Assert.Equal("TestPluginBeta", record.ModuleId);
        Assert.Equal("TestPluginBeta.Realtime", record.TaskKey);
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
        IDataPipelineService pipelineService,
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

        public Task WaitUntilDurableIdleAsync(TimeSpan timeout)
            => WaitForDurableQueuesIdleAsync(timeout);
    }

    private sealed class TestPluginWorkflowCellData : CellDataBase
    {
        public override string ProcessType => "TestPlugin";
    }

    private sealed class ObservedDataPipelineService(IDataPipelineService inner) : IDataPipelineService
    {
        private readonly TaskCompletionSource _secondRecordDequeued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _currentDrainCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _dequeuedCount;

        public Task SecondRecordDequeued => _secondRecordDequeued.Task;

        public Task CurrentDrainCompleted => _currentDrainCompleted.Task;

        public int PendingCount => inner.PendingCount;

        public int OverflowCount => inner.OverflowCount;

        public int SpillCount => inner.SpillCount;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
            => inner.EnqueueAsync(record, cancellationToken);

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            var dequeued = inner.TryDequeue(out record);
            if (dequeued && Interlocked.Increment(ref _dequeuedCount) == 2)
            {
                _secondRecordDequeued.TrySetResult();
            }

            return dequeued;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _dequeuedCount) >= 2)
            {
                _currentDrainCompleted.TrySetResult();
            }

            return inner.WaitToReadAsync(cancellationToken);
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
