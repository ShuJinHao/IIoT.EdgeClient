using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
﻿using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class RetryTaskCloudMesBehaviorTests
{
    [Fact]
    public async Task MesRetry_WhenHeartbeatRecovers_ShouldResetAbandonedRecordsAndRetryThem()
    {

        var logger = new FakeLogService();
        var retryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeMesFallbackBufferStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);
        var mesConsumer = new FakeMesConsumer();
        retryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData
            {
                Barcode = "BC-MES-001",
                DeviceName = "PLC-A",
                CompletedTime = DateTime.UtcNow
            }),
            FailedTarget = "MES",
            ErrorMessage = "abandoned",
            RetryCount = 21,
            NextRetryTime = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow
        });

        var task = new TestableMesRetryTask(
            logger,
            retryStore,
            fallbackStore,
            mesConsumer,
            heartbeatStore: heartbeatStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, retryStore.ResetAllAbandonedCallCount);
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Contains(1, retryStore.DeletedIds);
    }

    [Fact]
    public async Task MesRetry_WhenAbandonedRecordsExpired_ShouldCleanupDaily()
    {
        var logger = new FakeLogService();
        var retryStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeMesFallbackBufferStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);
        retryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{}",
            FailedTarget = "MES",
            ErrorMessage = "abandoned",
            RetryCount = 21,
            NextRetryTime = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddDays(-31)
        });

        var task = new TestableMesRetryTask(
            logger,
            retryStore,
            fallbackStore,
            new FakeMesConsumer(),
            heartbeatStore: heartbeatStore);
        task.MarkAvailableForCleanupTest();

        await task.ExecuteOnceAsync();

        Assert.Equal(1, retryStore.DeleteExpiredAbandonedCallCount);
        Assert.Empty(retryStore.PendingRecords);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenBatchSucceeds_ShouldDeleteBatchRecordsAndContinueOthers()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var deviceService = CreateOnlineDeviceService();
        var cloudBatch = new FakeCloudBatchConsumer();
        cloudBatch.EnqueueResult(true);
        var cloudConsumer = new FakeCloudConsumer();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);

        failedStore.PendingRecords.Add(CreateFailedRecord(1, "Cloud", "Cloud", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "INJ-1" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(2, "Cloud", "Cloud", 1, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "INJ-2" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(3, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-3" }));

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudBatch.ProcessBatchCallCount);
        Assert.Single(cloudBatch.ReceivedBatches);
        Assert.Equal(2, cloudBatch.ReceivedBatches[0].Count);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
        Assert.Contains(1L, failedStore.DeletedIds);
        Assert.Contains(2L, failedStore.DeletedIds);
        Assert.Contains(3L, failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
    }

    [Fact]
    public async Task CloudRetry_WhenPayloadIsPermanentlyInvalid_ShouldDeadLetterOnceWithoutBackoff()
    {
        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(
            CloudCallOutcome.InvalidPayload,
            "pass_station_completed_time_required"));
        var deadLetterStore = new FakeCloudDeadLetterStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(
            9,
            "Cloud",
            "Cloud",
            0,
            "OtherProcess",
            new TestCellData { Barcode = "INVALID-9" }));
        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: deadLetterStore);

        await task.ExecuteOnceAsync();
        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudConsumer.ProcessCallCount);
        Assert.Contains(9L, failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        var deadLetter = Assert.Single(deadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.InvalidPayload), deadLetter.FailureStage);
        Assert.Equal("pass_station_completed_time_required", deadLetter.FailureReason);
        Assert.Equal(9L, deadLetter.SourceRecordId);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenPayloadIsPermanentlyInvalid_ShouldDeadLetterEverySourceOnceWithoutFallbackOrBackoff()
    {
        var failedStore = new FakeFailedRecordStore();
        var cloudBatch = new FakeCloudBatchConsumer();
        cloudBatch.EnqueueResult(CloudCallResult.Failure(
            CloudCallOutcome.InvalidPayload,
            "pass_station_barcode_too_long"));
        var cloudConsumer = new FakeCloudConsumer();
        var fallbackStore = new FakeCloudFallbackBufferStore();
        var deadLetterStore = new FakeCloudDeadLetterStore();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        failedStore.PendingRecords.Add(CreateFailedRecord(
            91,
            "Cloud",
            "Cloud",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "INVALID-91" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(
            92,
            "Cloud",
            "Cloud",
            3,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "INVALID-92" }));
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            fallbackStore,
            CreateOnlineDeviceService(),
            cloudConsumer,
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: deadLetterStore,
            processIntegrationRegistry: integrationRegistry);

        await task.ExecuteOnceAsync();
        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudBatch.ProcessBatchCallCount);
        Assert.Equal(2, Assert.Single(cloudBatch.ReceivedBatches).Count);
        Assert.Equal(0, cloudConsumer.ProcessCallCount);
        Assert.Equal([91L, 92L], failedStore.DeletedIds.Order().ToArray());
        Assert.Empty(failedStore.Updates);
        Assert.Equal(0, fallbackStore.SaveCallCount);
        Assert.Equal(
            [91L, 92L],
            deadLetterStore.Records.Select(record => record.SourceRecordId).Order().ToArray());
        Assert.All(deadLetterStore.Records, record =>
        {
            Assert.Equal(nameof(DeadLetterStage.InvalidPayload), record.FailureStage);
            Assert.Equal("pass_station_barcode_too_long", record.FailureReason);
        });
    }

    [Fact]
    public async Task CloudBatchRetry_WhenRecordsHaveMixedValidity_ShouldDeadLetterOnlyInvalidAndUploadValid()
    {
        var failedStore = new FakeFailedRecordStore();
        var cloudBatch = new FakeCloudBatchConsumer
        {
            ValidateRecord = record => record.CellData is TestProcessCellData data &&
                                       data.Barcode.StartsWith("INVALID", StringComparison.Ordinal)
                ? CloudCallResult.Failure(CloudCallOutcome.InvalidPayload, "pass_station_barcode_invalid")
                : CloudCallResult.Success()
        };
        var deadLetterStore = new FakeCloudDeadLetterStore();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        failedStore.PendingRecords.Add(CreateFailedRecord(
            93,
            "Cloud",
            "Cloud",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "VALID-93" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(
            94,
            "Cloud",
            "Cloud",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "INVALID-94" }));
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry,
            deadLetterStore: deadLetterStore);

        await task.ExecuteOnceAsync();

        var uploaded = Assert.Single(cloudBatch.ReceivedBatches);
        Assert.Equal(
            "VALID-93",
            Assert.IsType<TestProcessCellData>(Assert.Single(uploaded).CellData).Barcode);
        Assert.Equal([93L, 94L], failedStore.DeletedIds.Order().ToArray());
        var deadLetter = Assert.Single(deadLetterStore.Records);
        Assert.Equal(94L, deadLetter.SourceRecordId);
        Assert.Equal("pass_station_barcode_invalid", deadLetter.FailureReason);
        Assert.DoesNotContain(deadLetterStore.Records, static record => record.SourceRecordId == 93);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenBatchFails_ShouldBackoffBatchRecordsAndKeepThem()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var deviceService = CreateOnlineDeviceService();
        var cloudBatch = new FakeCloudBatchConsumer();
        cloudBatch.EnqueueResult(CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "batch_http_failure"));
        var cloudConsumer = new FakeCloudConsumer();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);

        failedStore.PendingRecords.Add(CreateFailedRecord(10, "Cloud", "Cloud", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "INJ-10" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(11, "Cloud", "Cloud", 2, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "INJ-11" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(12, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-12" }));

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudBatch.ProcessBatchCallCount);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);

        Assert.DoesNotContain(10L, failedStore.DeletedIds);
        Assert.DoesNotContain(11L, failedStore.DeletedIds);
        Assert.Contains(12L, failedStore.DeletedIds);

        Assert.True(failedStore.Updates.TryGetValue(10, out var update10));
        Assert.True(failedStore.Updates.TryGetValue(11, out var update11));
        Assert.Equal(1, update10!.RetryCount);
        Assert.Equal(3, update11!.RetryCount);
        Assert.Contains("batch_http_failure", update10.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("batch_http_failure", update11.ErrorMessage, StringComparison.Ordinal);

        var delay10 = (update10.NextRetryTime - before).TotalSeconds;
        var delay11 = (update11.NextRetryTime - before).TotalSeconds;
        Assert.InRange(delay10, 20, 40);
        Assert.InRange(delay11, 20, 40);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenRegistryMarksProcessAsBatch_ShouldBatchNonInjectionRecords()
    {

        var failedStore = new FakeFailedRecordStore();
        var cloudBatch = new FakeCloudBatchConsumer();
        cloudBatch.EnqueueResult(true);
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader("OtherProcess", ProcessUploadMode.Batch);

        failedStore.PendingRecords.Add(CreateFailedRecord(31, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-31" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(32, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-32" }));

        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, cloudBatch.ProcessBatchCallCount);
        Assert.Equal(2, cloudBatch.ReceivedBatches[0].Count);
        Assert.Contains(31L, failedStore.DeletedIds);
        Assert.Contains(32L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenSameProcessContainsMultiplePlcs_ShouldSplitBatchesByPlc()
    {
        var failedStore = new FakeFailedRecordStore();
        var cloudBatch = new FakeCloudBatchConsumer();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader("OtherProcess", ProcessUploadMode.Batch);

        var first = CreateFailedRecord(33, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-33" });
        first.NetworkDeviceId = 1001;
        first.DeviceName = "PLC-A";
        var second = CreateFailedRecord(34, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-34" });
        second.NetworkDeviceId = 1002;
        second.DeviceName = "PLC-B";
        failedStore.PendingRecords.Add(first);
        failedStore.PendingRecords.Add(second);

        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        await task.ExecuteOnceAsync();

        Assert.Equal(2, cloudBatch.ProcessBatchCallCount);
        Assert.All(cloudBatch.ReceivedBatches, batch => Assert.Single(batch));
        Assert.Contains(cloudBatch.ReceivedBatches, batch => batch[0].DeviceName == "PLC-A");
        Assert.Contains(cloudBatch.ReceivedBatches, batch => batch[0].DeviceName == "PLC-B");
        Assert.Contains(33L, failedStore.DeletedIds);
        Assert.Contains(34L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenRegistryMarksTestProcessAsSingle_ShouldRetryRecordsIndividually()
    {

        var failedStore = new FakeFailedRecordStore();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader("OtherProcess", ProcessUploadMode.Single);
        var cloudConsumer = new FakeCloudConsumer();

        failedStore.PendingRecords.Add(CreateFailedRecord(41, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-41" }));
        failedStore.PendingRecords.Add(CreateFailedRecord(42, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-42" }));

        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        await task.ExecuteOnceAsync();

        Assert.Equal(0, cloudConsumer.ProcessedRecords.Count(x => string.Equals(x.CellData.ProcessType, TestProcessCellData.ProcessTypeKey, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, cloudConsumer.ProcessCallCount);
        Assert.Contains(41L, failedStore.DeletedIds);
        Assert.Contains(42L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenUploadGateIsBlocked_ShouldKeepRecordsWithoutBackoff()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(51, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-51" }));

        var deviceService = new FakeDeviceService();
        deviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.UploadTokenRejected
        });

        var cloudConsumer = new FakeCloudConsumer();
        var cloudBatch = new FakeCloudBatchConsumer();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        await task.ExecuteOnceAsync();

        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        Assert.Equal(0, cloudConsumer.ProcessCallCount);
        Assert.Equal(0, cloudBatch.ProcessBatchCallCount);
    }

    [Fact]
    public async Task CloudRetry_WhenUploadGateIsBlocked_ShouldReportWaitingForRecoveryRuntime()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(52, "Cloud", "Cloud", 0, "OtherProcess", new TestCellData { Barcode = "ST-52" }));

        var deviceService = new FakeDeviceService();
        deviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.UploadTokenRejected
        });

        var diagnosticsStore = new FakeCloudDiagnosticsStore();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            diagnosticsStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(CloudRetryRuntimeState.WaitingForRecovery, diagnosticsStore.Snapshot.RuntimeState);
    }

    [Fact]
    public async Task MesRetry_ShouldRunWhenCloudIsBlocked()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(61, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-61" }));

        var mesConsumer = new FakeMesConsumer();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Contains(61L, failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
    }

    [Fact]
    public async Task MesRetry_WhenProcessRecordIsRetried_ShouldKeepFullPayload()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(
            601,
            "MES",
            "MES",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData
            {
                Barcode = "BAR-RETRY-601",
                WorkOrderNo = "WO-RETRY-601",
                DeviceName = "PLC-H",
                DeviceCode = "CLIENT-H",
                CompletedTime = new DateTime(2026, 5, 3, 8, 30, 0, DateTimeKind.Utc),
                ScanTime = new DateTime(2026, 5, 3, 8, 29, 0, DateTimeKind.Utc),
                MeasurementValue = 31.5
            }));

        var mesConsumer = new FakeMesConsumer();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer);

        await task.ExecuteOnceAsync();

        var processed = Assert.IsType<TestProcessCellData>(
            Assert.Single(mesConsumer.ProcessedRecords).CellData);
        Assert.Equal("BAR-RETRY-601", processed.Barcode);
        Assert.Equal("WO-RETRY-601", processed.WorkOrderNo);
        Assert.Equal("CLIENT-H", processed.DeviceCode);
        Assert.Equal(31.5, processed.MeasurementValue);
        Assert.Equal(new DateTime(2026, 5, 3, 8, 29, 0, DateTimeKind.Utc), processed.ScanTime);
        Assert.Contains(601L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenUploadGateIsBlocked_ShouldNotBlockMesFallbackRecovery()
    {

        var cloudRetryStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 501,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "CLOUD-501" }),
            FailedTarget = "Cloud",
            ErrorMessage = "cloud-seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var blockedDeviceService = new FakeDeviceService();
        blockedDeviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.UploadTokenRejected
        });

        var cloudConsumer = new FakeCloudConsumer();
        var cloudTask = new TestableCloudRetryTask(
            new FakeLogService(),
            cloudRetryStore,
            cloudFallbackStore,
            blockedDeviceService,
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        var mesRetryStore = new FakeFailedRecordStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        mesFallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 601,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-601" }),
            FailedTarget = "MES",
            ErrorMessage = "mes-seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        var mesConsumer = new FakeMesConsumer();
        var mesTask = new TestableMesRetryTask(
            new FakeLogService(),
            mesRetryStore,
            mesFallbackStore,
            mesConsumer);

        await cloudTask.ExecuteOnceAsync();
        await mesTask.ExecuteOnceAsync();

        Assert.Equal(501L, Assert.Single(cloudFallbackStore.Records).Id);
        Assert.Empty(cloudFallbackStore.DeletedIds);
        Assert.Equal(0, cloudConsumer.ProcessCallCount);
        Assert.Contains(601L, mesFallbackStore.DeletedIds);
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Empty(mesRetryStore.PendingRecords);
    }

    [Fact]
    public async Task MesRetry_WhenMesUploadDisabled_ShouldLeaveBacklogUntouched()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(601, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-601" }));
        var diagnosticsStore = new FakeMesRetryDiagnosticsStore();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            new FakeMesConsumer(),
            diagnosticsStore,
            runtimeConfig: new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    MesUploadEnabled = false
                }
            });

        await task.ExecuteOnceAsync();

        Assert.Single(failedStore.PendingRecords);
        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        Assert.Empty(failedStore.ReleasedClaimTokens);
        Assert.Equal(MesRetryRuntimeState.Idle, diagnosticsStore.Snapshot.RuntimeState);
    }

    [Fact]
    public async Task MesRetry_WhenMesUploadDisabled_ShouldLeaveFallbackBacklogUntouched()
    {

        var failedStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeMesFallbackBufferStore();
        fallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 604,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-604" }),
            FailedTarget = "MES",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            fallbackStore,
            mesConsumer,
            runtimeConfig: new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    MesUploadEnabled = false
                }
            });

        await task.ExecuteOnceAsync();

        Assert.Equal(604L, Assert.Single(fallbackStore.Records).Id);
        Assert.Empty(fallbackStore.DeletedIds);
        Assert.Empty(failedStore.PendingRecords);
        Assert.Equal(0, mesConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task MesRetry_WhenHeartbeatIsNotReady_ShouldLeaveBacklogUntouched()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(602, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-602" }));
        var diagnosticsStore = new FakeMesRetryDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer,
            diagnosticsStore,
            heartbeatStore: heartbeatStore);

        await task.ExecuteOnceAsync();

        Assert.Single(failedStore.PendingRecords);
        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        Assert.Equal(0, mesConsumer.ProcessCallCount);
        Assert.Equal(MesRetryRuntimeState.Backoff, diagnosticsStore.Snapshot.RuntimeState);
    }

    [Fact]
    public async Task MesRetry_WhenHeartbeatIsNotReady_ShouldLeaveFallbackBacklogUntouched()
    {

        var failedStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeMesFallbackBufferStore();
        fallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 605,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-605" }),
            FailedTarget = "MES",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            fallbackStore,
            mesConsumer,
            heartbeatStore: heartbeatStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(605L, Assert.Single(fallbackStore.Records).Id);
        Assert.Empty(fallbackStore.DeletedIds);
        Assert.Empty(failedStore.PendingRecords);
        Assert.Equal(0, mesConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task MesRetry_WhenHeartbeatRecovers_ShouldUploadPendingRecords()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(603, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-603" }));
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer,
            heartbeatStore: heartbeatStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Contains(603L, failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
    }

    [Fact]
    public async Task MesRetry_WhenHeartbeatRecovers_ShouldRecoverFallbackAndUpload()
    {

        var failedStore = new FakeFailedRecordStore();
        var fallbackStore = new FakeMesFallbackBufferStore();
        fallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 606,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-606" }),
            FailedTarget = "MES",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            fallbackStore,
            mesConsumer,
            heartbeatStore: heartbeatStore);

        await task.ExecuteOnceAsync();

        Assert.Contains(606L, fallbackStore.DeletedIds);
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Empty(failedStore.PendingRecords);
    }

    [Fact]
    public async Task MesRetry_WhenFailureOccurs_ShouldIncreaseRetryCountAndBackoff()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(62, "MES", "MES", 4, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-62" }));

        var mesConsumer = new FakeMesConsumer();
        mesConsumer.EnqueueResult(false);

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer);

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        Assert.True(failedStore.Updates.TryGetValue(62, out var update));
        Assert.Equal(5, update!.RetryCount);
        Assert.InRange((update.NextRetryTime - before).TotalSeconds, 20, 40);
    }

    [Fact]
    public async Task MesRetry_WhenFailureOccurs_ShouldMoveRuntimeStateToLastFailed()
    {

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(63, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-63" }));

        var diagnosticsStore = new FakeMesRetryDiagnosticsStore();
        var mesConsumer = new FakeMesConsumer();
        mesConsumer.EnqueueResult(false);

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer,
            diagnosticsStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(MesRetryRuntimeState.LastFailed, diagnosticsStore.Snapshot.RuntimeState);
    }

    [Fact]
    public async Task MesRetry_ShouldRecoverFallbackRecordsIntoMainRetryStore()
    {

        var failedStore = new FakeFailedRecordStore();
        var mesFallbackStore = new FakeMesFallbackBufferStore();
        mesFallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 100,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "BC-MES-100", WorkOrderNo = "WO-MES-100" }),
            FailedTarget = "MES",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        var mesConsumer = new FakeMesConsumer();

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            mesFallbackStore,
            mesConsumer);

        await task.ExecuteOnceAsync();

        Assert.Contains(100L, mesFallbackStore.DeletedIds);
        Assert.Equal(1, mesConsumer.ProcessCallCount);
        Assert.Empty(failedStore.PendingRecords);
    }

    [Fact]
    public async Task CloudRetry_WhenBacklogIsOlderThan24Hours_ShouldDrainRetryAndFallbackRecords()
    {

        var oldTime = DateTime.UtcNow.AddHours(-25);
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 701,
            Channel = "Cloud",
            ProcessType = "TestProcess",
            CellDataJson = SerializeCellData(new TestCellData { Barcode = "ST-701" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 3,
            NextRetryTime = oldTime,
            CreatedAt = oldTime
        });

        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 901,
            ProcessType = "TestProcess",
            CellDataJson = SerializeCellData(new TestCellData { Barcode = "ST-901" }),
            FailedTarget = "Cloud",
            ErrorMessage = "fallback-seed",
            CreatedAt = oldTime
        });

        var cloudConsumer = new FakeCloudConsumer();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            cloudFallbackStore,
            CreateOnlineDeviceService(),
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        await task.ExecuteOnceAsync();

        Assert.Equal(2, cloudConsumer.ProcessCallCount);
        Assert.Empty(failedStore.PendingRecords);
        Assert.Contains(901L, cloudFallbackStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenFallbackRehydrateHitsRetryCapacity_ShouldKeepFallbackRecordBuffered()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(150, "Cloud", "Cloud", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "INJ-150" }));

        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 201,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "BC-CLOUD-201", WorkOrderNo = "WO-201" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var diagnosticsStore = new FakeCloudDiagnosticsStore();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        var capacityGuard = CreateCapacityGuard(
            logger,
            failedStore,
            new FakeFailedRecordStore(),
            cloudFallbackStore,
            new FakeMesFallbackBufferStore(),
            diagnosticsStore,
            new FakeMesRetryDiagnosticsStore(),
            configure: options => options.Cloud.RetryTotalLimit = 1);

        var cloudConsumer = new FakeCloudConsumer();
        var cloudBatchConsumer = new FakeCloudBatchConsumer();
        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            cloudFallbackStore,
            CreateOnlineDeviceService(),
            cloudConsumer,
            cloudBatchConsumer,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            diagnosticsStore: diagnosticsStore,
            processIntegrationRegistry: integrationRegistry,
            capacityGuard: capacityGuard);

        await task.ExecuteOnceAsync();

        Assert.DoesNotContain(201L, cloudFallbackStore.DeletedIds);
        Assert.Equal(0, failedStore.SaveCallCount);
        Assert.Equal(0, cloudConsumer.ProcessCallCount);
        Assert.Equal(1, cloudBatchConsumer.ProcessBatchCallCount);
        Assert.Equal(201L, Assert.Single(cloudFallbackStore.Records).Id);
    }

    [Fact]
    public async Task MesRetry_WhenFallbackRehydrateHitsRetryCapacity_ShouldKeepFallbackRecordBuffered()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(402, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-402" }));

        var mesFallbackStore = new FakeMesFallbackBufferStore();
        mesFallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 502,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-502" }),
            FailedTarget = "MES",
            ErrorMessage = "fallback-seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-3)
        });

        var diagnosticsStore = new FakeMesRetryDiagnosticsStore();
        var capacityGuard = CreateCapacityGuard(
            logger,
            new FakeFailedRecordStore(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            mesFallbackStore,
            new FakeCloudDiagnosticsStore(),
            diagnosticsStore,
            configure: options => options.Mes.RetryTotalLimit = 1);

        var mesConsumer = new FakeMesConsumer();
        var task = new TestableMesRetryTask(
            logger,
            failedStore,
            mesFallbackStore,
            mesConsumer,
            diagnosticsStore,
            capacityGuard: capacityGuard);

        await task.ExecuteOnceAsync();

        Assert.DoesNotContain(502L, mesFallbackStore.DeletedIds);
        Assert.Equal(502L, Assert.Single(mesFallbackStore.Records).Id);
        Assert.Equal(1, mesConsumer.ProcessCallCount);
    }

    [Fact]
    public async Task MesRetry_WhenRetryCapacityRecovers_ShouldClearBlockedDiagnostics()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(401, "MES", "MES", 0, TestProcessCellData.ProcessTypeKey, new TestProcessCellData { Barcode = "MES-401" }));

        var diagnosticsStore = new FakeMesRetryDiagnosticsStore();
        var capacityGuard = CreateCapacityGuard(
            logger,
            new FakeFailedRecordStore(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            new FakeMesFallbackBufferStore(),
            new FakeCloudDiagnosticsStore(),
            diagnosticsStore,
            configure: options => options.Mes.RetryTotalLimit = 1);

        var blockedReason = await capacityGuard.GetMesRetryBlockReasonAsync(TestProcessCellData.ProcessTypeKey);
        Assert.Equal("total", blockedReason);
        Assert.True(diagnosticsStore.Snapshot.IsCapacityBlocked);

        var task = new TestableMesRetryTask(
            logger,
            failedStore,
            new FakeMesFallbackBufferStore(),
            new FakeMesConsumer(),
            diagnosticsStore,
            capacityGuard: capacityGuard);

        await task.ExecuteOnceAsync();

        Assert.False(diagnosticsStore.Snapshot.IsCapacityBlocked);
        Assert.NotNull(diagnosticsStore.Snapshot.LastCapacityBlockAt);
    }

    [Fact]
    public async Task MesRetry_WhenBacklogIsOlderThan24Hours_ShouldDrainRetryAndFallbackRecords()
    {

        var oldTime = DateTime.UtcNow.AddHours(-25);
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 801,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-801", WorkOrderNo = "WO-801" }),
            FailedTarget = "MES",
            ErrorMessage = "seed",
            RetryCount = 2,
            NextRetryTime = oldTime,
            CreatedAt = oldTime
        });

        var mesFallbackStore = new FakeMesFallbackBufferStore();
        mesFallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 1001,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "MES-1001", WorkOrderNo = "WO-1001" }),
            FailedTarget = "MES",
            ErrorMessage = "fallback-seed",
            CreatedAt = oldTime
        });

        var mesConsumer = new FakeMesConsumer();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            mesFallbackStore,
            mesConsumer);

        await task.ExecuteOnceAsync();

        Assert.Equal(2, mesConsumer.ProcessCallCount);
        Assert.Empty(failedStore.PendingRecords);
        Assert.Contains(1001L, mesFallbackStore.DeletedIds);
    }

    [Fact]
    public async Task CloudChannel_ShouldInvokeDeviceLogAndCapacityRetryHooks()
    {
        var failedStore = new FakeFailedRecordStore();
        var logger = new FakeLogService();
        var deviceLogSync = new FakeDeviceLogSyncTask { RetryResult = false };
        var capacitySync = new FakeCapacitySyncTask { RetryResult = true };

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            deviceLogSync,
            capacitySync);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, deviceLogSync.RetryBufferCallCount);
        Assert.Equal(1, capacitySync.RetryBufferCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("设备日志缓冲补传已暂停或失败", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CloudChannel_WhenFallbackContainsMixedValidity_ShouldContinueRecoveringRemainingRecords()
    {

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 201,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            ErrorMessage = "seed-1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 202,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = SerializeCellData(new TestProcessCellData { Barcode = "BC-CLOUD-202", WorkOrderNo = "WO-202" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed-2",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        });

        var cloudConsumer = new FakeCloudConsumer();
        var cloudBatchConsumer = new FakeCloudBatchConsumer();
        var deadLetterStore = new FakeCloudDeadLetterStore();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            cloudFallbackStore,
            CreateOnlineDeviceService(),
            cloudConsumer,
            cloudBatchConsumer,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry,
            deadLetterStore: deadLetterStore);

        await task.ExecuteOnceAsync();

        Assert.Contains(201L, cloudFallbackStore.DeletedIds);
        Assert.Contains(202L, cloudFallbackStore.DeletedIds);
        Assert.Single(deadLetterStore.Records);
        Assert.Equal(0, cloudConsumer.ProcessCallCount);
        Assert.Equal(1, cloudBatchConsumer.ProcessBatchCallCount);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("Cloud", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FallbackDeserializeFailure_ShouldUseSeparateDeadLetterStoresAndSourceTables()
    {
        var cloudFallbackStore = new FakeCloudFallbackBufferStore();
        cloudFallbackStore.Records.Add(new CloudFallbackRecord
        {
            Id = 701,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            ErrorMessage = "cloud-seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            NetworkDeviceId = 7010,
            DeviceName = "PLC-CLOUD-701",
            ModuleId = "TestPluginBeta",
            TaskKey = "TestPluginBeta.Realtime",
            PlanSessionId = "SESSION-CLOUD-701",
            MainPlanCode = "PLAN-CLOUD-701",
            TraceBatchNumber = "TRACE-CLOUD-701"
        });
        var cloudDeadLetterStore = new FakeCloudDeadLetterStore();
        var cloudTask = new TestableCloudRetryTask(
            new FakeLogService(),
            new FakeFailedRecordStore(),
            cloudFallbackStore,
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: cloudDeadLetterStore);

        var mesFallbackStore = new FakeMesFallbackBufferStore();
        mesFallbackStore.Records.Add(new MesFallbackRecord
        {
            Id = 801,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "MES",
            ErrorMessage = "mes-seed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
            NetworkDeviceId = 8010,
            DeviceName = "PLC-MES-801",
            ModuleId = "TestPluginBeta",
            TaskKey = "TestPluginBeta.Realtime",
            PlanSessionId = "SESSION-MES-801",
            MainPlanCode = "PLAN-MES-801",
            TraceBatchNumber = "TRACE-MES-801"
        });
        var mesDeadLetterStore = new FakeMesDeadLetterStore();
        var mesTask = new TestableMesRetryTask(
            new FakeLogService(),
            new FakeFailedRecordStore(),
            mesFallbackStore,
            new FakeMesConsumer(),
            deadLetterStore: mesDeadLetterStore);

        await cloudTask.ExecuteOnceAsync();
        await mesTask.ExecuteOnceAsync();

        var cloudDeadLetter = Assert.Single(cloudDeadLetterStore.Records);
        Assert.Equal("Cloud", cloudDeadLetter.FailedTarget);
        Assert.Equal("cloud_fallback_records", cloudDeadLetter.SourceTable);
        Assert.Equal(701L, cloudDeadLetter.SourceRecordId);
        Assert.Equal(nameof(DeadLetterStage.FallbackRecoverDeserialize), cloudDeadLetter.FailureStage);
        Assert.Equal(7010, cloudDeadLetter.NetworkDeviceId);
        Assert.Equal("PLC-CLOUD-701", cloudDeadLetter.DeviceName);
        Assert.Equal("TestPluginBeta", cloudDeadLetter.ModuleId);
        Assert.Equal("TestPluginBeta.Realtime", cloudDeadLetter.TaskKey);
        Assert.Equal("SESSION-CLOUD-701", cloudDeadLetter.PlanSessionId);
        Assert.Equal("PLAN-CLOUD-701", cloudDeadLetter.MainPlanCode);
        Assert.Equal("TRACE-CLOUD-701", cloudDeadLetter.TraceBatchNumber);
        Assert.Contains(701L, cloudFallbackStore.DeletedIds);

        var mesDeadLetter = Assert.Single(mesDeadLetterStore.Records);
        Assert.Equal("MES", mesDeadLetter.FailedTarget);
        Assert.Equal("mes_fallback_records", mesDeadLetter.SourceTable);
        Assert.Equal(801L, mesDeadLetter.SourceRecordId);
        Assert.Equal(nameof(DeadLetterStage.FallbackRecoverDeserialize), mesDeadLetter.FailureStage);
        Assert.Equal(8010, mesDeadLetter.NetworkDeviceId);
        Assert.Equal("PLC-MES-801", mesDeadLetter.DeviceName);
        Assert.Equal("TestPluginBeta", mesDeadLetter.ModuleId);
        Assert.Equal("TestPluginBeta.Realtime", mesDeadLetter.TaskKey);
        Assert.Equal("SESSION-MES-801", mesDeadLetter.PlanSessionId);
        Assert.Equal("PLAN-MES-801", mesDeadLetter.MainPlanCode);
        Assert.Equal("TRACE-MES-801", mesDeadLetter.TraceBatchNumber);
        Assert.Contains(801L, mesFallbackStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenDeserializeFails_ShouldMoveRecordToDeadLetterAndDeleteSource()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 301,
            Channel = "Cloud",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterStore = new FakeCloudDeadLetterStore();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: deadLetterStore);

        await task.ExecuteOnceAsync();

        var deadLetter = Assert.Single(deadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.RetryDeserialize), deadLetter.FailureStage);
        Assert.Equal("failed_cloud_records", deadLetter.SourceTable);
        Assert.Equal("Cloud", deadLetter.FailedTarget);
        Assert.Contains(301L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task MesRetry_WhenDeserializeFails_ShouldMoveRecordToMesDeadLetterAndDeleteSource()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 305,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "MES",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterStore = new FakeMesDeadLetterStore();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            new FakeMesConsumer(),
            deadLetterStore: deadLetterStore);

        await task.ExecuteOnceAsync();

        var deadLetter = Assert.Single(deadLetterStore.Records);
        Assert.Equal(nameof(DeadLetterStage.RetryDeserialize), deadLetter.FailureStage);
        Assert.Equal("failed_mes_records", deadLetter.SourceTable);
        Assert.Equal("MES", deadLetter.FailedTarget);
        Assert.Contains(305L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task MesRetry_ShouldResetAbandonedRecordsOnRecovery()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 306,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "MES",
            ErrorMessage = "abandoned",
            RetryCount = 21,
            NextRetryTime = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddDays(-31)
        });

        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            new FakeMesConsumer());

        await task.ExecuteOnceAsync();

        Assert.Equal(1, failedStore.ResetAllAbandonedCallCount);
        Assert.Empty(failedStore.PendingRecords);
        Assert.Contains(306, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenDeadLetterSaveFails_ShouldBackoffRecordAndReleaseClaim()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 302,
            Channel = "Cloud",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 2,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterStore = new FakeCloudDeadLetterStore
        {
            SaveException = new InvalidOperationException("dead-letter down")
        };
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: deadLetterStore,
            criticalWriter: criticalWriter);

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        Assert.DoesNotContain(302L, failedStore.DeletedIds);
        Assert.True(failedStore.Updates.TryGetValue(302, out var update));
        Assert.Equal(3, update!.RetryCount);
        Assert.Contains("死信持久化也失败", update.ErrorMessage, StringComparison.Ordinal);
        Assert.InRange((update.NextRetryTime - before).TotalSeconds, 20, 40);
        Assert.Single(criticalWriter.Writes);

        var reclaimedBatch = await failedStore.ClaimPendingBatchAsync("Cloud", 10);
        Assert.NotNull(reclaimedBatch);
        Assert.Contains(reclaimedBatch!.Records, record => record.Id == 302);
    }

    [Fact]
    public async Task CloudBatchRetry_WhenDeadLetterSaveFails_ShouldBackoffRecordAndReleaseClaim()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 303,
            Channel = "Cloud",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 0,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterStore = new FakeCloudDeadLetterStore
        {
            SaveException = new InvalidOperationException("dead-letter down")
        };
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry,
            deadLetterStore: deadLetterStore,
            criticalWriter: criticalWriter);

        await task.ExecuteOnceAsync();

        Assert.DoesNotContain(303L, failedStore.DeletedIds);
        Assert.True(failedStore.Updates.TryGetValue(303, out var update));
        Assert.Equal(1, update!.RetryCount);
        Assert.Contains("死信持久化也失败", update.ErrorMessage, StringComparison.Ordinal);
        Assert.Single(criticalWriter.Writes);

        var reclaimedBatch = await failedStore.ClaimPendingBatchAsync("Cloud", 10);
        Assert.NotNull(reclaimedBatch);
        Assert.Contains(reclaimedBatch!.Records, record => record.Id == 303);
    }

    [Fact]
    public async Task MesRetry_WhenDeadLetterSaveFails_ShouldBackoffRecordAndReleaseClaim()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 304,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "MES",
            ErrorMessage = "seed",
            RetryCount = 1,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterStore = new FakeMesDeadLetterStore
        {
            SaveException = new InvalidOperationException("dead-letter down")
        };
        var criticalWriter = new FakeCriticalPersistenceFallbackWriter();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            new FakeMesConsumer(),
            deadLetterStore: deadLetterStore,
            criticalWriter: criticalWriter);

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        Assert.DoesNotContain(304L, failedStore.DeletedIds);
        Assert.True(failedStore.Updates.TryGetValue(304, out var update));
        Assert.Equal(2, update!.RetryCount);
        Assert.Contains("死信持久化也失败", update.ErrorMessage, StringComparison.Ordinal);
        Assert.InRange((update.NextRetryTime - before).TotalSeconds, 20, 40);
        Assert.Single(criticalWriter.Writes);

        var reclaimedBatch = await failedStore.ClaimPendingBatchAsync("MES", 10);
        Assert.NotNull(reclaimedBatch);
        Assert.Contains(reclaimedBatch!.Records, record => record.Id == 304);
    }

    [Fact]
    public async Task CloudRetry_WhenCallerCancelsAsClaimReturns_ShouldReleaseWithoutAnyRecordSideEffect()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 330,
            Channel = "Cloud",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "Cloud",
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1)
        });
        var consumer = new FakeCloudConsumer();
        var deadLetter = new FakeCloudDeadLetterStore();
        using var cancellation = new CancellationTokenSource();
        failedStore.ClaimPendingBatchReturning = cancellation.Cancel;
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            consumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterStore: deadLetter);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.ProcessRecordsAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Single(failedStore.ReleasedClaimTokens);
        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        Assert.Equal(0, consumer.ProcessCallCount);
        Assert.Equal(0, deadLetter.SaveCallCount);
    }

    [Fact]
    public async Task MesRetry_WhenCallerCancelsAsClaimReturns_ShouldReleaseWithoutAnyRecordSideEffect()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 331,
            Channel = "MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = "MES",
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1)
        });
        var consumer = new FakeMesConsumer();
        var deadLetter = new FakeMesDeadLetterStore();
        using var cancellation = new CancellationTokenSource();
        failedStore.ClaimPendingBatchReturning = cancellation.Cancel;
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            consumer,
            deadLetterStore: deadLetter);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.ProcessRecordsAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Single(failedStore.ReleasedClaimTokens);
        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        Assert.Equal(0, consumer.ProcessCallCount);
        Assert.Equal(0, deadLetter.SaveCallCount);
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("MES")]
    public async Task RetryProcessor_WhenCallerIsPreCanceled_ShouldNotClaim(string channel)
    {
        var store = new FakeFailedRecordStore();
        var process = CreateRecordProcessor(channel, store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(0, store.ClaimCallCount);
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("MES")]
    public async Task RetryProcessor_WhenCallerCancelsAsSuccessfulDeleteReturns_ShouldRethrowAndStop(string channel)
    {
        var store = new FakeFailedRecordStore();
        store.PendingRecords.Add(CreateFailedRecord(
            340,
            channel,
            channel,
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "DELETE-CANCEL" }));
        using var cancellation = new CancellationTokenSource();
        store.DeleteReturning = cancellation.Cancel;
        var process = CreateRecordProcessor(channel, store);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Single(store.DeletedIds);
        Assert.Equal(1, store.ReleaseClaimCallCount);
        Assert.Empty(store.Updates);
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("MES")]
    public async Task RetryProcessor_WhenCallerCancelsAsFailureUpdateReturns_ShouldRethrowAndStop(string channel)
    {
        var store = new FakeFailedRecordStore();
        store.PendingRecords.Add(CreateFailedRecord(
            341,
            channel,
            channel,
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "UPDATE-CANCEL" }));
        using var cancellation = new CancellationTokenSource();
        store.UpdateRetryReturning = cancellation.Cancel;
        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(false);
        var mesConsumer = new FakeMesConsumer();
        mesConsumer.EnqueueResult(false);
        var process = CreateRecordProcessor(channel, store, cloudConsumer, mesConsumer);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Single(store.Updates);
        Assert.Equal(1, store.ReleaseClaimCallCount);
        Assert.Empty(store.DeletedIds);
    }

    [Theory]
    [InlineData("Cloud")]
    [InlineData("MES")]
    public async Task RetryProcessor_WhenCallerCancelsAsDeadLetterReturns_ShouldReleaseAndRethrow(string channel)
    {
        var store = new FakeFailedRecordStore();
        store.PendingRecords.Add(new FailedCellRecord
        {
            Id = 342,
            Channel = channel,
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellDataJson = "{bad-json",
            FailedTarget = channel,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1)
        });
        using var cancellation = new CancellationTokenSource();
        var cloudDeadLetter = new FakeCloudDeadLetterStore { SaveReturning = cancellation.Cancel };
        var mesDeadLetter = new FakeMesDeadLetterStore { SaveReturning = cancellation.Cancel };
        var critical = new FakeCriticalPersistenceFallbackWriter();
        var process = CreateRecordProcessor(
            channel,
            store,
            cloudDeadLetter: cloudDeadLetter,
            mesDeadLetter: mesDeadLetter,
            criticalWriter: critical);

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => process(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(1, cloudDeadLetter.SaveCallCount + mesDeadLetter.SaveCallCount);
        Assert.Single(store.ReleasedClaimTokens);
        Assert.Empty(store.DeletedIds);
        Assert.Empty(store.Updates);
        Assert.Empty(critical.Writes);
    }

    [Fact]
    public async Task CloudRetry_WhenCallerCancelsClaimedConsumerCall_ShouldReleaseClaimAndRethrow()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(
            305,
            "Cloud",
            "Cloud",
            0,
            "OtherProcess",
            new TestCellData { Barcode = "CANCEL-CLOUD-305" }));
        var processStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudConsumer = new FakeCloudConsumer
        {
            ProcessStarted = processStarted,
            ProcessWait = neverComplete.Task
        };
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());
        using var cancellation = new CancellationTokenSource();

        var processing = task.ProcessRecordsAsync(cancellation.Token);
        await processStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Single(failedStore.ReleasedClaimTokens);
        Assert.Empty(failedStore.DeletedIds);
        var reclaimed = await failedStore.ClaimPendingBatchAsync("Cloud", 10);
        Assert.NotNull(reclaimed);
        Assert.Contains(reclaimed!.Records, static record => record.Id == 305);
    }

    [Fact]
    public async Task MesRetry_WhenCallerCancelsClaimedConsumerCall_ShouldReleaseClaimAndRethrow()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(
            306,
            "MES",
            "MES",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "CANCEL-MES-306" }));
        var processStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesConsumer = new FakeMesConsumer
        {
            ProcessStarted = processStarted,
            ProcessWait = neverComplete.Task
        };
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer);
        using var cancellation = new CancellationTokenSource();

        var processing = task.ProcessRecordsAsync(cancellation.Token);
        await processStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Single(failedStore.ReleasedClaimTokens);
        Assert.Empty(failedStore.DeletedIds);
        var reclaimed = await failedStore.ClaimPendingBatchAsync("MES", 10);
        Assert.NotNull(reclaimed);
        Assert.Contains(reclaimed!.Records, static record => record.Id == 306);
    }

    [Fact]
    public async Task CloudRetry_WhenDeviceStatusEndpointMissing_ShouldReleaseClaimAndKeepRecordPending()
    {
        var failedStore = new FakeFailedRecordStore();
        var record = CreateFailedRecord(
            307,
            "Cloud",
            "Cloud",
            0,
            TestProcessCellData.ProcessTypeKey,
            new TestProcessCellData { Barcode = "STATUS-307" });
        record.TaskKey = "TestPlugin.EquipmentStatus";
        failedStore.PendingRecords.Add(record);
        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(
            CloudCallOutcome.SkippedUploadNotReady,
            "cloud_plc_device_status_endpoint_missing"));
        var cloudBatch = new FakeCloudBatchConsumer();
        var integrationRegistry = new FakeProcessIntegrationRegistry();
        integrationRegistry.RegisterCloudUploader(TestProcessCellData.ProcessTypeKey, ProcessUploadMode.Batch);
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            cloudConsumer,
            cloudBatch,
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            processIntegrationRegistry: integrationRegistry);

        var result = await task.ProcessRecordsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudRetryProcessResult.PauseForRecovery, result);
        Assert.Equal(1, cloudConsumer.ProcessCallCount);
        Assert.Equal(0, cloudBatch.ProcessBatchCallCount);
        Assert.Single(failedStore.ReleasedClaimTokens);
        Assert.Empty(failedStore.DeletedIds);
        Assert.Empty(failedStore.Updates);
        var reclaimed = await failedStore.ClaimPendingBatchAsync("Cloud", 10);
        Assert.NotNull(reclaimed);
        Assert.Contains(reclaimed!.Records, static pending => pending.Id == 307);
    }

    private static FakeDeviceService CreateOnlineDeviceService()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        return deviceService;
    }

    private static Func<CancellationToken, Task> CreateRecordProcessor(
        string channel,
        FakeFailedRecordStore store,
        FakeCloudConsumer? cloudConsumer = null,
        FakeMesConsumer? mesConsumer = null,
        FakeCloudDeadLetterStore? cloudDeadLetter = null,
        FakeMesDeadLetterStore? mesDeadLetter = null,
        FakeCriticalPersistenceFallbackWriter? criticalWriter = null)
    {
        if (channel.Equals("Cloud", StringComparison.OrdinalIgnoreCase))
        {
            var task = new TestableCloudRetryTask(
                new FakeLogService(),
                store,
                new FakeCloudFallbackBufferStore(),
                CreateOnlineDeviceService(),
                cloudConsumer ?? new FakeCloudConsumer(),
                new FakeCloudBatchConsumer(),
                new FakeDeviceLogSyncTask(),
                new FakeCapacitySyncTask(),
                deadLetterStore: cloudDeadLetter,
                criticalWriter: criticalWriter);
            return async cancellationToken =>
                await task.ProcessRecordsAsync(cancellationToken).ConfigureAwait(false);
        }

        var mesTask = new TestableMesRetryTask(
            new FakeLogService(),
            store,
            new FakeMesFallbackBufferStore(),
            mesConsumer ?? new FakeMesConsumer(),
            deadLetterStore: mesDeadLetter,
            criticalWriter: criticalWriter);
        return async cancellationToken =>
            await mesTask.ProcessRecordsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FailedCellRecord CreateFailedRecord(
        long id,
        string channel,
        string failedTarget,
        int retryCount,
        string processType,
        CellDataBase cellData)
        => new()
        {
            Id = id,
            Channel = channel,
            ProcessType = processType,
            CellDataJson = SerializeCellData(cellData),
            FailedTarget = failedTarget,
            ErrorMessage = "seed",
            RetryCount = retryCount,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };

    private static string SerializeCellData(CellDataBase cellData)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(cellData, cellData.GetType(), jsonOptions);
    }

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

    private sealed class TestableCloudRetryTask
    {
        private readonly CloudRetryTask _inner;
        private readonly CloudRetryRecordProcessor _recordProcessor;

        public TestableCloudRetryTask(
            FakeLogService logger,
            FakeFailedRecordStore retryStore,
            FakeCloudFallbackBufferStore fallbackStore,
            FakeDeviceService deviceService,
            FakeCloudConsumer cloudConsumer,
            FakeCloudBatchConsumer cloudBatchConsumer,
            FakeDeviceLogSyncTask deviceLogSync,
            FakeCapacitySyncTask capacitySync,
            FakeCloudDiagnosticsStore? diagnosticsStore = null,
            FakeProcessIntegrationRegistry? processIntegrationRegistry = null,
            FakeCloudDeadLetterStore? deadLetterStore = null,
            FakeCriticalPersistenceFallbackWriter? criticalWriter = null,
            DataPipelineCapacityGuard? capacityGuard = null)
        {
            fallbackStore.RetryStore = retryStore;
            var cloudDiagnosticsStore = diagnosticsStore ?? new FakeCloudDiagnosticsStore();
            var cloudDeadLetterStore = deadLetterStore ?? new FakeCloudDeadLetterStore();
            var fallbackWriter = criticalWriter ?? new FakeCriticalPersistenceFallbackWriter();
            var cloudCapacityGuard = capacityGuard ?? CreateCapacityGuard(
                logger,
                retryStore,
                new FakeFailedRecordStore(),
                fallbackStore,
                new FakeMesFallbackBufferStore(),
                cloudDiagnosticsStore,
                new FakeMesRetryDiagnosticsStore());
            var cloudDeadLetterWriter = new DataPipelineDeadLetterWriter();
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();

            _recordProcessor = new CloudRetryRecordProcessor(
                logger,
                retryStore,
                cloudDeadLetterStore,
                fallbackWriter,
                cloudConsumer,
                cloudBatchConsumer,
                cloudDiagnosticsStore,
                new DefaultRetryBackoffStrategy(),
                cloudDeadLetterWriter,
                new DefaultDataPipelineConsumerInvoker(),
                cellDataJsonSerializer,
                processIntegrationRegistry);
            _inner = new CloudRetryTask(
                logger,
                deviceService,
                deviceLogSync,
                capacitySync,
                cloudDiagnosticsStore,
                cloudCapacityGuard,
                new CloudFallbackRecoveryService(
                    logger,
                    fallbackStore,
                    cloudDeadLetterStore,
                    fallbackWriter,
                    cloudCapacityGuard,
                    cloudDeadLetterWriter,
                    cellDataJsonSerializer),
                _recordProcessor,
                new CloudRetryHousekeepingService(
                    logger,
                    retryStore,
                    cloudDiagnosticsStore));
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();

        public Task<CloudRetryProcessResult> ProcessRecordsAsync(CancellationToken cancellationToken)
            => _recordProcessor.ProcessAsync(cancellationToken);
    }

    private sealed class TestableMesRetryTask
    {
        private readonly MesRetryTask _inner;
        private readonly MesRetryHousekeepingService _housekeepingService;
        private readonly MesRetryRecordProcessor _recordProcessor;

        public TestableMesRetryTask(
            FakeLogService logger,
            FakeFailedRecordStore retryStore,
            FakeMesFallbackBufferStore fallbackStore,
            FakeMesConsumer mesConsumer,
            FakeMesRetryDiagnosticsStore? diagnosticsStore = null,
            FakeMesDeadLetterStore? deadLetterStore = null,
            FakeCriticalPersistenceFallbackWriter? criticalWriter = null,
            FakeLocalSystemRuntimeConfigService? runtimeConfig = null,
            DataPipelineCapacityGuard? capacityGuard = null,
            FakeExternalHeartbeatStateStore? heartbeatStore = null)
        {
            fallbackStore.RetryStore = retryStore;
            var mesDiagnosticsStore = diagnosticsStore ?? new FakeMesRetryDiagnosticsStore();
            var mesHeartbeatStore = heartbeatStore ?? new FakeExternalHeartbeatStateStore();
            if (heartbeatStore is null)
            {
                mesHeartbeatStore.MarkReady(ExternalSystemKind.Mes);
            }

            var mesDeadLetterStore = deadLetterStore ?? new FakeMesDeadLetterStore();
            var fallbackWriter = criticalWriter ?? new FakeCriticalPersistenceFallbackWriter();
            var mesCapacityGuard = capacityGuard ?? CreateCapacityGuard(
                logger,
                new FakeFailedRecordStore(),
                retryStore,
                new FakeCloudFallbackBufferStore(),
                fallbackStore,
                new FakeCloudDiagnosticsStore(),
                mesDiagnosticsStore);
            var deadLetterWriter = new DataPipelineDeadLetterWriter();
            var consumerInvoker = new DefaultDataPipelineConsumerInvoker();
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();
            _housekeepingService = new MesRetryHousekeepingService(
                logger,
                retryStore,
                mesDiagnosticsStore);

            _recordProcessor = new MesRetryRecordProcessor(
                logger,
                retryStore,
                mesDeadLetterStore,
                fallbackWriter,
                mesConsumer,
                new DefaultRetryBackoffStrategy(),
                deadLetterWriter,
                consumerInvoker,
                cellDataJsonSerializer);
            _inner = new MesRetryTask(
                logger,
                runtimeConfig ?? new FakeLocalSystemRuntimeConfigService(),
                mesDiagnosticsStore,
                mesCapacityGuard,
                mesHeartbeatStore,
                new MesFallbackRecoveryService(
                    logger,
                    fallbackStore,
                    mesDeadLetterStore,
                    fallbackWriter,
                    mesCapacityGuard,
                    deadLetterWriter,
                    cellDataJsonSerializer),
                _recordProcessor,
                _housekeepingService);
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();

        public Task<MesRetryProcessResult> ProcessRecordsAsync(CancellationToken cancellationToken)
            => _recordProcessor.ProcessAsync(cancellationToken);

        public void MarkAvailableForCleanupTest()
        {
            typeof(RetryTaskBase<MesRetryRuntimeState, MesRetryProcessResult>)
                .GetField("_wasUnavailable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(_inner, false);
            typeof(RetryHousekeepingServiceBase<MesRetryRuntimeState>)
                .GetField("_lastAbandonedCleanupDateUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(_housekeepingService, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        }
    }

    private static ICellDataJsonSerializer CreateCellDataJsonSerializer()
    {
        var typeRegistry = new CellDataTypeRegistry();
        typeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);
        typeRegistry.Register<TestCellData>("OtherProcess");
        return new CellDataJsonSerializer(typeRegistry);
    }
}
