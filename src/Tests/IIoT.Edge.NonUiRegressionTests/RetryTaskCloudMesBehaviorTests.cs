using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class RetryTaskCloudMesBehaviorTests
{
    [Fact]
    public async Task MesRetry_WhenHeartbeatRecovers_ShouldResetAbandonedRecordsAndRetryThem()
    {
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
    public async Task CloudBatchRetry_WhenBatchFails_ShouldBackoffBatchRecordsAndKeepThem()
    {
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
    public async Task CloudRetry_WhenRegistryMarksTestProcessAsSingle_ShouldRetryRecordsIndividually()
    {
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
    public async Task MesRetry_WhenHomogenizationOutboundRecordIsRetried_ShouldKeepFullPayload()
    {
        CellDataTypeRegistry.Register<HomogenizationCellData>("Homogenization");

        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(CreateFailedRecord(
            601,
            "MES",
            "MES",
            0,
            "Homogenization",
            new HomogenizationCellData
            {
                TrayCode = "TRAY-H-RETRY",
                DeviceName = "PLC-H",
                DeviceCode = "CLIENT-H",
                CompletedTime = new DateTime(2026, 5, 3, 8, 30, 0, DateTimeKind.Utc),
                CntActualKg = 15,
                NmpActualKg = 18,
                GlueActualKg = 31,
                RealtimeSnapshot = new HomogenizationRealtimeSnapshot
                {
                    StirringSpeed = 120,
                    Temperature = 26,
                    Vacuum = -9
                },
                RecipeSnapshot = new HomogenizationRecipeSnapshot
                {
                    StirringSpeed = [10],
                    DispersionSpeed = [20],
                    Time = [30]
                },
                EquipmentStatusSnapshot = new HomogenizationEquipmentStatusSnapshot
                {
                    StatusCode = 1,
                    StatusText = "空闲",
                    Messages = ["空闲"]
                }
            }));

        var mesConsumer = new FakeMesConsumer();
        var task = new TestableMesRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeMesFallbackBufferStore(),
            mesConsumer);

        await task.ExecuteOnceAsync();

        var processed = Assert.IsType<HomogenizationCellData>(
            Assert.Single(mesConsumer.ProcessedRecords).CellData);
        Assert.Equal("TRAY-H-RETRY", processed.TrayCode);
        Assert.Equal("CLIENT-H", processed.DeviceCode);
        Assert.Equal(15d, processed.CntActualKg);
        Assert.Equal(18d, processed.NmpActualKg);
        Assert.Equal(31d, processed.GlueActualKg);
        Assert.Equal(120, processed.RealtimeSnapshot!.StirringSpeed);
        Assert.Equal(26, processed.RealtimeSnapshot.Temperature);
        Assert.Equal(-9, processed.RealtimeSnapshot.Vacuum);
        Assert.Equal(10, processed.RecipeSnapshot!.StirringSpeed[0]);
        Assert.Equal(20, processed.RecipeSnapshot.DispersionSpeed[0]);
        Assert.Equal(30, processed.RecipeSnapshot.Time[0]);
        Assert.Equal(1, processed.EquipmentStatusSnapshot!.StatusCode);
        Assert.Equal("空闲", processed.EquipmentStatusSnapshot.StatusText);
        Assert.Contains(601L, failedStore.DeletedIds);
    }

    [Fact]
    public async Task CloudRetry_WhenUploadGateIsBlocked_ShouldNotBlockMesFallbackRecovery()
    {
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestCellData>("OtherProcess");

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        CellDataTypeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);

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
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已进入 Cloud 死信表", StringComparison.Ordinal));
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
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
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
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
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
        Assert.Contains(701L, cloudFallbackStore.DeletedIds);

        var mesDeadLetter = Assert.Single(mesDeadLetterStore.Records);
        Assert.Equal("MES", mesDeadLetter.FailedTarget);
        Assert.Equal("mes_fallback_records", mesDeadLetter.SourceTable);
        Assert.Equal(801L, mesDeadLetter.SourceRecordId);
        Assert.Equal(nameof(DeadLetterStage.FallbackRecoverDeserialize), mesDeadLetter.FailureStage);
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
                    cloudDeadLetterWriter),
                new CloudRetryRecordProcessor(
                    logger,
                    retryStore,
                    cloudDeadLetterStore,
                    fallbackWriter,
                    cloudConsumer,
                    cloudBatchConsumer,
                    cloudDiagnosticsStore,
                    new DefaultRetryBackoffStrategy(),
                    cloudDeadLetterWriter,
                    processIntegrationRegistry),
                new CloudRetryHousekeepingService(
                    logger,
                    retryStore,
                    cloudDiagnosticsStore));
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();
    }

    private sealed class TestableMesRetryTask
    {
        private readonly MesRetryTask _inner;

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

            _inner = new MesRetryTask(
                logger,
                retryStore,
                fallbackStore,
                deadLetterStore ?? new FakeMesDeadLetterStore(),
                criticalWriter ?? new FakeCriticalPersistenceFallbackWriter(),
                mesConsumer,
                runtimeConfig ?? new FakeLocalSystemRuntimeConfigService(),
                mesDiagnosticsStore,
                capacityGuard ?? CreateCapacityGuard(
                    logger,
                    new FakeFailedRecordStore(),
                    retryStore,
                    new FakeCloudFallbackBufferStore(),
                    fallbackStore,
                    new FakeCloudDiagnosticsStore(),
                    mesDiagnosticsStore),
                mesHeartbeatStore,
                new DefaultRetryBackoffStrategy(),
                new DataPipelineDeadLetterWriter());
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();

        public void MarkAvailableForCleanupTest()
        {
            typeof(MesRetryTask)
                .GetField("_wasUnavailable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(_inner, false);
            typeof(MesRetryTask)
                .GetField("_lastAbandonedCleanupDateUtc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(_inner, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        }
    }
}
