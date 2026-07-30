using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Text.Json;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class ProcessQueueTaskBehaviorTests
{
    [Fact]
    public async Task DurableWorkers_AfterStopAndRestart_ShouldConsumeCloudAndMesAgainWithoutCompensation()
    {
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
            "Cloud", 10, "Cloud", result: true, ConsumerFailureMode.Durable);
        var mesConsumer = new FakeCellDataConsumer(
            "MES", 20, "MES", result: true, ConsumerFailureMode.Durable);
        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [cloudConsumer, mesConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        var firstRecord = CreateTestPluginRecord();
        firstRecord.CellData.UploadTargets = DataPipelineUploadTargets.All;
        await pipeline.EnqueueAsync(firstRecord, TestContext.Current.CancellationToken);
        using (var firstStop = new CancellationTokenSource())
        {
            var firstRun = task.StartAsync(firstStop.Token);
            await WaitUntilAsync(
                () => cloudConsumer.ProcessCallCount == 1 && mesConsumer.ProcessCallCount == 1,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));
            await firstStop.CancelAsync();
            await firstStop.CancelAsync();
            await firstRun.WaitAsync(TestContext.Current.CancellationToken);
        }

        var secondRecord = CreateTestPluginRecord();
        secondRecord.CellData.UploadTargets = DataPipelineUploadTargets.All;
        await pipeline.EnqueueAsync(secondRecord, TestContext.Current.CancellationToken);
        using (var secondStop = new CancellationTokenSource())
        {
            var secondRun = task.StartAsync(secondStop.Token);
            await WaitUntilAsync(
                () => cloudConsumer.ProcessCallCount == 2 && mesConsumer.ProcessCallCount == 2,
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));
            await secondStop.CancelAsync();
            await secondStop.CancelAsync();
            await secondRun.WaitAsync(TestContext.Current.CancellationToken);
        }

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

    [Fact]
    public async Task DurableConsumerInvalidPayload_ShouldPersistDirectlyToDeadLetterExactlyOnce()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var consumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: false,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) => throw new DataPipelineNonRetryableException("pass_station_cell_result_required"));
        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [consumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);
        await pipeline.EnqueueAsync(CreateTestPluginRecord(), TestContext.Current.CancellationToken);

        await task.ExecuteOnceAsync();
        await task.ExecuteOnceAsync();

        Assert.Equal(1, consumer.ProcessCallCount);
        Assert.Empty(cloudRetryStore.PendingRecords);
        Assert.Empty(cloudFallbackStore.Records);
        var deadLetter = Assert.Single(cloudDeadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.InvalidPayload), deadLetter.FailureStage);
        Assert.Equal("pass_station_cell_result_required", deadLetter.FailureReason);
        Assert.Empty(mesRetryStore.PendingRecords);
        Assert.Empty(mesFallbackStore.Records);
        Assert.Empty(mesDeadLetterStore.Records);
        Assert.Empty(criticalWriter.Writes);
    }

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
    public async Task TestPluginRecords_WhenRuntimeIsCancelledWithActiveAndQueuedDurableItems_ShouldPersistEachTargetExactlyOnce()
    {
        var cloudConsumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudConsumerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var observedPipeline = new ObservedDataPipelineService(pipeline, expectedDequeuedCount: 4);
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
                cloudConsumerStarted.TrySetResult();
                try
                {
                    await neverComplete.Task.WaitAsync(cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    cloudConsumerCancelled.TrySetResult();
                    throw;
                }
            });
        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                mesConsumerStarted.TrySetResult();
                try
                {
                    await neverComplete.Task.WaitAsync(cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    mesConsumerCancelled.TrySetResult();
                    throw;
                }
            });
        var task = new TestableProcessQueueTask(
            logger,
            observedPipeline,
            [cloudConsumer, mesConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);
        var records = new[]
        {
            CreateTestPluginRecord("TestPlugin.Cloud.Active", DataPipelineUploadTargets.Cloud),
            CreateTestPluginRecord("TestPlugin.Cloud.Queued", DataPipelineUploadTargets.Cloud),
            CreateTestPluginRecord("TestPlugin.Mes.Active", DataPipelineUploadTargets.Mes),
            CreateTestPluginRecord("TestPlugin.Mes.Queued", DataPipelineUploadTargets.Mes)
        };
        var enqueueResults = new List<DataPipelineEnqueueResult>();
        foreach (var record in records)
        {
            enqueueResults.Add(await pipeline.EnqueueAsync(
                record,
                TestContext.Current.CancellationToken));
        }

        using var cancellation = new CancellationTokenSource();

        var runtime = task.StartAsync(cancellation.Token);
        try
        {
            await cloudConsumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await mesConsumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.AllExpectedRecordsDequeued.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.CurrentDrainCompleted.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, pipeline.PendingCount);
            await cancellation.CancelAsync();

            await cloudConsumerCancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
            await mesConsumerCancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));

            Assert.All(enqueueResults, result => Assert.True(result.IsDurablyAccepted));
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            Assert.Equal(1, mesConsumer.ProcessCallCount);
            Assert.Equal(0, pipeline.PendingCount);
            Assert.Equal(2, cloudRetryStore.SaveCallCount);
            Assert.Equal(2, mesRetryStore.SaveCallCount);
            Assert.Equal(
                ["TestPlugin.Cloud.Active", "TestPlugin.Cloud.Queued"],
                cloudRetryStore.PendingRecords.Select(record => record.TaskKey).Order().ToArray());
            Assert.All(cloudRetryStore.PendingRecords, record =>
            {
                Assert.Equal("Cloud", record.Channel);
                Assert.Equal("Cloud", record.FailedTarget);
            });
            Assert.Equal(
                ["TestPlugin.Mes.Active", "TestPlugin.Mes.Queued"],
                mesRetryStore.PendingRecords.Select(record => record.TaskKey).Order().ToArray());
            Assert.All(mesRetryStore.PendingRecords, record =>
            {
                Assert.Equal("MES", record.Channel);
                Assert.Equal("MES", record.FailedTarget);
            });
            var persistedTaskKeys = cloudRetryStore.PendingRecords
                .Concat(mesRetryStore.PendingRecords)
                .Select(record => record.TaskKey)
                .ToArray();
            Assert.Equal(4, persistedTaskKeys.Length);
            Assert.Equal(4, persistedTaskKeys.Distinct(StringComparer.Ordinal).Count());
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
    public async Task DurableShutdown_WhenWaitToReadReturnsNormallyAsRuntimeCancels_ShouldAwaitWorkerPersistence()
    {
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retrySaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRetrySaveToReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompleteConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logger = new FakeLogService();
        var innerPipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        using var runtimeCancellation = new CancellationTokenSource();
        var pipeline = new CancelWhenWaitToReadReturnsPipeline(
            innerPipeline,
            consumerStarted.Task,
            runtimeCancellation);
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveStarted = retrySaveStarted,
            SaveWait = allowRetrySaveToReturn.Task
        };
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
                await neverCompleteConsumer.Task.WaitAsync(cancellationToken);
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
        await innerPipeline.EnqueueAsync(
            CreateTestPluginRecord("TestPlugin.Cloud.WaitToReadRace", DataPipelineUploadTargets.Cloud),
            TestContext.Current.CancellationToken);

        var runtime = task.StartAsync(runtimeCancellation.Token);
        try
        {
            await retrySaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            Assert.True(runtimeCancellation.IsCancellationRequested);
            Assert.False(runtime.IsCompleted);

            allowRetrySaveToReturn.TrySetResult();
            await runtime.WaitAsync(TestContext.Current.CancellationToken);

            Assert.True(runtime.IsCompletedSuccessfully);
            Assert.Equal(1, cloudConsumer.ProcessCallCount);
            Assert.Equal(1, cloudRetryStore.SaveCallCount);
            Assert.Single(cloudRetryStore.PendingRecords);
            Assert.Empty(cloudFallbackStore.Records);
            Assert.Empty(cloudDeadLetterStore.Records);
            Assert.Empty(criticalWriter.Writes);
        }
        finally
        {
            allowRetrySaveToReturn.TrySetResult();
            await runtimeCancellation.CancelAsync();
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DurableShutdown_WhenChannelDeadlineExpires_ShouldWriteRecoverableEvidenceWithinOneTotalBudget()
    {
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retrySaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompleteConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompleteRetrySave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var observedPipeline = new ObservedDataPipelineService(pipeline, expectedDequeuedCount: 2);
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveStarted = retrySaveStarted,
            SaveWait = neverCompleteRetrySave.Task
        };
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
                    await neverCompleteConsumer.Task.WaitAsync(cancellationToken);
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
            criticalWriter,
            runtimeOptions: new DataPipelineRuntimeOptions { DurableShutdownTimeoutSeconds = 30 },
            shutdownTimeProvider: timeProvider);
        var records = new[]
        {
            CreateTestPluginRecord("TestPlugin.Cloud.Timeout.Active", DataPipelineUploadTargets.Cloud),
            CreateTestPluginRecord("TestPlugin.Cloud.Timeout.Queued", DataPipelineUploadTargets.Cloud)
        };
        foreach (var record in records)
        {
            await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);
        }

        using var cancellation = new CancellationTokenSource();
        var runtime = task.StartAsync(cancellation.Token);
        try
        {
            await consumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.AllExpectedRecordsDequeued.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.CurrentDrainCompleted.WaitAsync(TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await consumerCancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
            await retrySaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            Assert.False(runtime.IsCompleted);
            timeProvider.Advance(TimeSpan.FromSeconds(30));

            await runtime.WaitAsync(TestContext.Current.CancellationToken);
            await task.WaitUntilDurableIdleAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, cloudRetryStore.SaveCallCount);
            Assert.Empty(cloudRetryStore.PendingRecords);
            Assert.Empty(mesRetryStore.PendingRecords);
            Assert.Equal(2, criticalWriter.Writes.Count);
            foreach (var record in records)
            {
                var evidence = Assert.Single(
                    criticalWriter.Writes,
                    entry => ReadJsonString(entry.Details, "taskKey") == record.TaskKey);
                AssertRecoverableShutdownEvidence(evidence, record, "Cloud", "failed_cloud_records");
            }

            Assert.Empty(cloudFallbackStore.Records);
            Assert.Empty(mesFallbackStore.Records);
            Assert.Empty(cloudDeadLetterStore.Records);
            Assert.Empty(mesDeadLetterStore.Records);
        }
        finally
        {
            await cancellation.CancelAsync();
            timeProvider.Advance(TimeSpan.FromDays(1));
            await runtime.WaitAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task DurableShutdown_WhenRecoverableEvidenceWriteFails_ShouldFaultRuntimeExplicitly()
    {
        var consumerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retrySaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompleteConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompleteRetrySave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(new FakeIngressOverflowPersistence(), logger);
        var observedPipeline = new ObservedDataPipelineService(pipeline, expectedDequeuedCount: 1);
        var cloudRetryStore = new FakeFailedRecordStore
        {
            SaveStarted = retrySaveStarted,
            SaveWait = neverCompleteRetrySave.Task
        };
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter
        {
            WriteException = new IOException("critical crash log unavailable")
        };
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: async (_, cancellationToken) =>
            {
                consumerStarted.TrySetResult();
                await neverCompleteConsumer.Task.WaitAsync(cancellationToken);
                return true;
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
            criticalWriter,
            runtimeOptions: new DataPipelineRuntimeOptions { DurableShutdownTimeoutSeconds = 30 },
            shutdownTimeProvider: timeProvider);
        await pipeline.EnqueueAsync(
            CreateTestPluginRecord("TestPlugin.Cloud.CriticalFailure", DataPipelineUploadTargets.Cloud),
            TestContext.Current.CancellationToken);

        using var cancellation = new CancellationTokenSource();
        var runtime = task.StartAsync(cancellation.Token);
        try
        {
            await consumerStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.AllExpectedRecordsDequeued.WaitAsync(TestContext.Current.CancellationToken);
            await observedPipeline.CurrentDrainCompleted.WaitAsync(TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();
            await retrySaveStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(runtime.IsCompleted);

            timeProvider.Advance(TimeSpan.FromSeconds(30));

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => runtime.WaitAsync(TestContext.Current.CancellationToken));
            Assert.Equal("DurableShutdownPersistenceException", exception.GetType().Name);
            Assert.Contains("durable shutdown", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(criticalWriter.Writes);
            Assert.Empty(cloudRetryStore.PendingRecords);
            Assert.Empty(mesRetryStore.PendingRecords);
            Assert.Empty(cloudFallbackStore.Records);
            Assert.Empty(mesFallbackStore.Records);
            Assert.Empty(cloudDeadLetterStore.Records);
            Assert.Empty(mesDeadLetterStore.Records);
        }
        finally
        {
            await cancellation.CancelAsync();
            timeProvider.Advance(TimeSpan.FromDays(1));
            try
            {
                await runtime.WaitAsync(TestContext.Current.CancellationToken);
            }
            catch
            {
                // The explicit fault is the contract under test.
            }
        }
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("MES")]
    public async Task TestPluginRecord_WhenDurableConsumerSelfCancels_ShouldCompensateOnlyTargetOutletOnce(
        string channel)
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
        var durableConsumer = new FakeCellDataConsumer(
            name: channel,
            order: 10,
            retryChannel: channel,
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
            [durableConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);
        var record = CreateTestPluginRecord();
        record.CellData.UploadTargets = channel == "Cloud"
            ? DataPipelineUploadTargets.Cloud
            : DataPipelineUploadTargets.Mes;
        var enqueueResult = await pipeline.EnqueueAsync(
            record,
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
            Assert.Equal(1, durableConsumer.ProcessCallCount);
            var targetRetryStore = channel == "Cloud" ? cloudRetryStore : mesRetryStore;
            var otherRetryStore = channel == "Cloud" ? mesRetryStore : cloudRetryStore;
            var retry = Assert.Single(targetRetryStore.PendingRecords);
            Assert.Equal(channel, retry.Channel);
            Assert.Equal(channel, retry.FailedTarget);
            Assert.Contains("provider cancelled itself", retry.ErrorMessage, StringComparison.Ordinal);
            Assert.Empty(otherRetryStore.PendingRecords);
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
    public async Task MesTarget_WhenDurableMesConsumerReturnsFalse_ShouldPersistOnlyMesRetryRecord()
    {
        var logger = new FakeLogService();
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var record = CreateRecord();
        record.CellData.UploadTargets = DataPipelineUploadTargets.Mes;
        await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);

        var mesConsumer = new FakeCellDataConsumer(
            name: "MES",
            order: 20,
            retryChannel: "MES",
            result: false,
            failureMode: ConsumerFailureMode.Durable);

        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [mesConsumer],
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDeadLetterStore,
            mesDeadLetterStore,
            criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.Empty(cloudRetryStore.PendingRecords);
        var retry = Assert.Single(mesRetryStore.PendingRecords);
        Assert.Equal("MES", retry.Channel);
        Assert.Equal("MES", retry.FailedTarget);
        AssertStoredContext(retry);
        Assert.Empty(cloudFallbackStore.Records);
        Assert.Empty(mesFallbackStore.Records);
    }

    [Theory]
    [InlineData("开始处理", true, 0)]
    [InlineData("已完成本地处理", true, 0)]
    [InlineData("准备写入", false, 1)]
    public async Task ProcessQueue_WhenRecordLogSubscriberThrows_ShouldStillDispatchOrCompensateExactlyOnce(
        string throwingLogMarker,
        bool consumerResult,
        int expectedRetryCount)
    {
        var logger = new FakeLogService();
        logger.EntryAdded += entry =>
        {
            if (entry.Message.Contains(throwingLogMarker, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"log subscriber failed at {throwingLogMarker}");
            }
        };
        var pipeline = new FakeDataPipelineService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var consumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: consumerResult,
            failureMode: ConsumerFailureMode.Durable);
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);
        var task = new TestableProcessQueueTask(
            logger,
            pipeline,
            [consumer],
            cloudRetryStore,
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter());

        await task.ExecuteOnceAsync();

        Assert.Equal(1, consumer.ProcessCallCount);
        Assert.Equal(expectedRetryCount, cloudRetryStore.PendingRecords.Count);
        Assert.Equal(0, pipeline.PendingCount);
    }

    [Fact]
    public async Task EnqueueAccepted_ShouldNotNotifyProductionViewUntilMainQueueConsumesRecord()
    {
        var pipeline = new FakeDataPipelineService();
        var uiConsumer = new FakeCellDataConsumer(
            name: "UI",
            order: 50,
            retryChannel: null,
            result: true,
            failureMode: ConsumerFailureMode.BestEffort);
        var enqueueResult = await pipeline.EnqueueAsync(
            CreateRecord(),
            TestContext.Current.CancellationToken);
        var task = new TestableProcessQueueTask(
            new FakeLogService(),
            pipeline,
            [uiConsumer],
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDeadLetterStore(),
            new FakeMesDeadLetterStore(),
            new FakeCriticalPersistenceFallbackWriter());

        Assert.True(enqueueResult.IsDurablyAccepted);
        Assert.Equal(1, pipeline.PendingCount);
        Assert.Equal(0, uiConsumer.ProcessCallCount);

        await task.ExecuteOnceAsync();

        Assert.Equal(0, pipeline.PendingCount);
        Assert.Equal(1, uiConsumer.ProcessCallCount);
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
        var neverCompletes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await pipeline.EnqueueAsync(CreateRecord(), TestContext.Current.CancellationToken);

        // Intentionally ignore the consumer token: this case verifies the queue's own per-call timeout.
        var cloudConsumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 10,
            retryChannel: "Cloud",
            result: true,
            failureMode: ConsumerFailureMode.Durable,
            processAsync: (_, _) => neverCompletes.Task);

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
    public async Task CascadingPersistence_WhenCanceledBeforeEntry_ShouldPerformNoPersistenceSideEffects()
    {
        var harness = CreateCascadingPersistenceHarness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: cancellation.Token));

        Assert.Equal(0, harness.CloudRetry.SaveCallCount);
        Assert.Equal(0, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenCanceledDuringRetryAwait_ShouldNotFallbackOrDeadLetter()
    {
        var harness = CreateCascadingPersistenceHarness();
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.CloudRetry.SaveStarted = retryStarted;
        harness.CloudRetry.SaveWait = neverComplete.Task;
        using var cancellation = new CancellationTokenSource();

        var persist = harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: cancellation.Token);
        await retryStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => persist);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(0, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenDeadlineCancelsAsRetryStoreReturns_ShouldCommitRetryExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        using var shutdownBudgetCancellation = new CancellationTokenSource();
        harness.CloudRetry.SaveReturning = shutdownBudgetCancellation.Cancel;

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: shutdownBudgetCancellation.Token);

        Assert.True(shutdownBudgetCancellation.IsCancellationRequested);
        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Single(harness.CloudRetry.PendingRecords);
        Assert.Equal(0, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenRetryCommitLogSubscriberThrows_ShouldKeepSingleRetryCommit()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.Logger.EntryAdded += _ => throw new InvalidOperationException("log subscriber failed");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Single(harness.CloudRetry.PendingRecords);
        Assert.Equal(0, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenRetryStoreSelfCancels_ShouldPersistFallbackExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        var storeCancellation = new OperationCanceledException("retry store canceled");
        harness.CloudRetry.SaveException = storeCancellation;

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenFallbackStoreSelfCancels_ShouldPersistDeadLetterExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        harness.CloudFallback.SaveException = new OperationCanceledException("fallback canceled itself");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenDeadlineCancelsAsFallbackStoreReturns_ShouldCommitFallbackExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        using var shutdownBudgetCancellation = new CancellationTokenSource();
        harness.CloudFallback.SaveReturning = shutdownBudgetCancellation.Cancel;

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: shutdownBudgetCancellation.Token);

        Assert.True(shutdownBudgetCancellation.IsCancellationRequested);
        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Single(harness.CloudFallback.Records);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenRetryFailureAndFallbackCommitLogsThrow_ShouldContinueAndCommitFallbackOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        harness.Logger.EntryAdded += _ => throw new InvalidOperationException("log subscriber failed");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Single(harness.CloudFallback.Records);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenRetryGuardFailureLogSubscriberThrows_ShouldStillCommitFallbackOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.CloudCountException = new IOException("retry guard unavailable");
        harness.Logger.EntryAdded += _ => throw new InvalidOperationException("log subscriber failed");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(0, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Single(harness.CloudFallback.Records);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task NonRetryablePersistence_WhenDeadLetterStoreSelfCancels_ShouldInvokeCriticalExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudDeadLetter.SaveException = new OperationCanceledException("deadletter canceled itself");

        var result = await harness.Writer.PersistNonRetryableAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "invalid_payload",
            "failed_cloud_cells",
            TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Single(harness.Critical.Writes);
    }

    [Fact]
    public async Task NonRetryablePersistence_WhenDeadlineCancelsAsDeadLetterReturns_ShouldCommitDeadLetterExactlyOnce()
    {
        var harness = CreateCascadingPersistenceHarness();
        using var cancellation = new CancellationTokenSource();
        harness.CloudDeadLetter.SaveReturning = cancellation.Cancel;

        var result = await harness.Writer.PersistNonRetryableAsync(
                CreateRecord(),
                DataPipelineRetryChannel.Cloud,
                "Cloud",
                "invalid_payload",
                "failed_cloud_cells",
                cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Single(harness.CloudDeadLetter.Records);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenDeadlineCancelsAsDeadLetterReturns_ShouldNotDoubleWriteCriticalEvidence()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        harness.CloudFallback.SaveException = new IOException("fallback unavailable");
        using var cancellation = new CancellationTokenSource();
        harness.CloudDeadLetter.SaveReturning = cancellation.Cancel;

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Single(harness.CloudDeadLetter.Records);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenDeadLetterCommitLogSubscriberThrows_ShouldKeepSingleDeadLetterCommit()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        harness.CloudFallback.SaveException = new IOException("fallback unavailable");
        harness.Logger.EntryAdded += _ => throw new InvalidOperationException("log subscriber failed");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.DurableShutdownPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(1, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Single(harness.CloudDeadLetter.Records);
        Assert.Empty(harness.Critical.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CascadingPersistence_WhenRetryCapacityProviderFails_ShouldContinueToFallback(bool selfCancel)
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.CloudCountException = selfCancel
            ? new OperationCanceledException("retry count canceled itself")
            : new IOException("retry count unavailable");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(0, harness.CloudRetry.SaveCallCount);
        Assert.Equal(1, harness.CloudFallback.SaveCallCount);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CascadingPersistence_WhenFallbackCapacityProviderFails_ShouldPersistDeadLetter(bool selfCancel)
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        harness.CloudFallback.CountException = selfCancel
            ? new OperationCanceledException("fallback count canceled itself")
            : new IOException("fallback count unavailable");

        var result = await harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(0, harness.CloudFallback.SaveCallCount);
        Assert.Equal(1, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task CascadingPersistence_WhenCanceledDuringFallbackAwait_ShouldNotDeadLetterOrCriticalFallback()
    {
        var harness = CreateCascadingPersistenceHarness();
        harness.CloudRetry.SaveException = new IOException("retry unavailable");
        var fallbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.CloudFallback.SaveStarted = fallbackStarted;
        harness.CloudFallback.SaveWait = neverComplete.Task;
        using var cancellation = new CancellationTokenSource();

        var persist = harness.Writer.PersistAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "failed",
            "failed_cloud_cells",
            DeadLetterStage.FallbackPersist,
            cancellationToken: cancellation.Token);
        await fallbackStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => persist);
        Assert.Equal(0, harness.CloudDeadLetter.SaveCallCount);
        Assert.Empty(harness.Critical.Writes);
    }

    [Fact]
    public async Task NonRetryablePersistence_WhenCanceledDuringDeadLetterAwait_ShouldNotInvokeCriticalFallback()
    {
        var harness = CreateCascadingPersistenceHarness();
        var deadLetterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.CloudDeadLetter.SaveStarted = deadLetterStarted;
        harness.CloudDeadLetter.SaveWait = neverComplete.Task;
        using var cancellation = new CancellationTokenSource();

        var persist = harness.Writer.PersistNonRetryableAsync(
            CreateRecord(),
            DataPipelineRetryChannel.Cloud,
            "Cloud",
            "invalid_payload",
            "failed_cloud_cells",
            cancellation.Token);
        await deadLetterStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => persist);
        Assert.Empty(harness.Critical.Writes);
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

    private static CellCompletedRecord CreateTestPluginRecord(
        string taskKey = "TestPlugin.Snapshot",
        DataPipelineUploadTargets uploadTargets = DataPipelineUploadTargets.Cloud)
        => new()
        {
            PlcCode = "PLC-TEST-01",
            NetworkDeviceId = 17,
            DeviceName = "PLC-TEST-01",
            ModuleId = "TestPlugin",
            TaskKey = taskKey,
            PlanSessionId = $"{taskKey}.Session",
            MainPlanCode = $"{taskKey}.Plan",
            TraceBatchNumber = $"{taskKey}.Trace",
            CreatedAtUtc = DateTime.UtcNow,
            CellData = new TestPluginWorkflowCellData
            {
                PlcDeviceId = 17,
                DeviceName = "PLC-TEST-01",
                DeviceCode = "PLC-TEST-01",
                CompletedTime = DateTime.UtcNow,
                CellResult = true,
                UploadTargets = uploadTargets
            }
        };

    private static string? ReadJsonString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(propertyName).GetString();
    }

    private static void AssertRecoverableShutdownEvidence(
        FakeCriticalPersistenceFallbackWriter.WriteEntry evidence,
        CellCompletedRecord expectedRecord,
        string expectedChannel,
        string expectedSourceTable)
    {
        Assert.Equal(
            $"DataPipeline.ProcessQueue.{expectedChannel}.ShutdownPersistenceFailed",
            evidence.Source);
        Assert.IsType<TimeoutException>(evidence.Exception);

        using var document = JsonDocument.Parse(evidence.Details);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expectedChannel, root.GetProperty("channel").GetString());
        Assert.Equal(expectedChannel, root.GetProperty("failedTarget").GetString());
        Assert.Equal(expectedSourceTable, root.GetProperty("sourceTable").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceRecordId").ValueKind);
        Assert.Equal(nameof(DeadLetterStage.DurableShutdownPersist), root.GetProperty("failureStage").GetString());
        Assert.Contains("shutdown", root.GetProperty("failureReason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedRecord.CellData.ProcessType, root.GetProperty("processType").GetString());
        Assert.Equal(expectedRecord.ResolveNetworkDeviceId(), root.GetProperty("networkDeviceId").GetInt32());
        Assert.Equal(expectedRecord.ResolveDeviceName(), root.GetProperty("deviceName").GetString());
        Assert.Equal(expectedRecord.ModuleId, root.GetProperty("moduleId").GetString());
        Assert.Equal(expectedRecord.TaskKey, root.GetProperty("taskKey").GetString());
        Assert.Equal(expectedRecord.PlanSessionId, root.GetProperty("planSessionId").GetString());
        Assert.Equal(expectedRecord.MainPlanCode, root.GetProperty("mainPlanCode").GetString());
        Assert.Equal(expectedRecord.TraceBatchNumber, root.GetProperty("traceBatchNumber").GetString());

        var registry = new CellDataTypeRegistry();
        registry.Register<TestPluginWorkflowCellData>(expectedRecord.CellData.ProcessType);
        var serializer = new CellDataJsonSerializer(registry);
        var cellDataJson = Assert.IsType<string>(root.GetProperty("cellDataJson").GetString());
        var restoredCellData = Assert.IsType<TestPluginWorkflowCellData>(
            serializer.Deserialize(expectedRecord.CellData.ProcessType, cellDataJson));
        Assert.Equal(expectedRecord.CellData.PlcDeviceId, restoredCellData.PlcDeviceId);
        Assert.Equal(expectedRecord.CellData.DeviceName, restoredCellData.DeviceName);
        Assert.Equal(expectedRecord.CellData.DeviceCode, restoredCellData.DeviceCode);
        Assert.Equal(expectedRecord.CellData.CellResult, restoredCellData.CellResult);
        Assert.Equal(expectedRecord.CellData.UploadTargets, restoredCellData.UploadTargets);
    }

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
            linkedCts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
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
        DataPipelineRuntimeOptions? runtimeOptions = null,
        TimeProvider? shutdownTimeProvider = null)
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
            runtimeOptions,
            shutdownTimeProvider)
    {
        public async Task ExecuteOnceAsync()
        {
            await base.ExecuteAsync();
            await WaitForDurableQueuesIdleAsync(TimeSpan.FromSeconds(5));
        }

        public Task WaitUntilDurableIdleAsync(TimeSpan timeout)
            => WaitForDurableQueuesIdleAsync(timeout);
    }

    public sealed class TestPluginWorkflowCellData : CellDataBase
    {
        public override string ProcessType => "TestPlugin";
    }

    private sealed class ObservedDataPipelineService(
        IDataPipelineService inner,
        int expectedDequeuedCount) : IDataPipelineService
    {
        private readonly TaskCompletionSource _allExpectedRecordsDequeued =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _currentDrainCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _dequeuedCount;

        public Task AllExpectedRecordsDequeued => _allExpectedRecordsDequeued.Task;

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
            if (dequeued && Interlocked.Increment(ref _dequeuedCount) == expectedDequeuedCount)
            {
                _allExpectedRecordsDequeued.TrySetResult();
            }

            return dequeued;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _dequeuedCount) >= expectedDequeuedCount)
            {
                _currentDrainCompleted.TrySetResult();
            }

            return inner.WaitToReadAsync(cancellationToken);
        }
    }

    private sealed class CancelWhenWaitToReadReturnsPipeline(
        IDataPipelineService inner,
        Task durableConsumerStarted,
        CancellationTokenSource runtimeCancellation) : IDataPipelineService
    {
        public int PendingCount => inner.PendingCount;

        public int OverflowCount => inner.OverflowCount;

        public int SpillCount => inner.SpillCount;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
            => inner.EnqueueAsync(record, cancellationToken);

        public bool TryDequeue(out CellCompletedRecord? record)
            => inner.TryDequeue(out record);

        public async ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        {
            await durableConsumerStarted.ConfigureAwait(false);
            await runtimeCancellation.CancelAsync();
            return true;
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

    private static CascadingPersistenceHarness CreateCascadingPersistenceHarness()
    {
        var logger = new FakeLogService();
        var cloudRetry = new FakeFailedRecordStore();
        var mesRetry = new FakeFailedRecordStore();
        var cloudFallback = new FakeCloudFallbackBufferStore();
        var mesFallback = new FakeMesFallbackBufferStore();
        var cloudDeadLetter = new FakeCloudDeadLetterStore();
        var mesDeadLetter = new FakeMesDeadLetterStore();
        var critical = new FakeCriticalPersistenceFallbackWriter();
        return new CascadingPersistenceHarness(
            CreatePersistenceWriter(
                logger,
                cloudRetry,
                mesRetry,
                cloudFallback,
                mesFallback,
                cloudDeadLetter,
                mesDeadLetter,
                critical,
                capacityGuard: null),
            logger,
            cloudRetry,
            cloudFallback,
            cloudDeadLetter,
            critical);
    }

    private sealed record CascadingPersistenceHarness(
        DataPipelineCascadingPersistenceWriter Writer,
        FakeLogService Logger,
        FakeFailedRecordStore CloudRetry,
        FakeCloudFallbackBufferStore CloudFallback,
        FakeCloudDeadLetterStore CloudDeadLetter,
        FakeCriticalPersistenceFallbackWriter Critical);
}
