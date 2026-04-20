using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Text.Json;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class RetryTaskBehaviorTests
{
    [Fact]
    public async Task Reconnect_ShouldResetAbandonedRecordsOnlyOnRecovery()
    {
        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var deviceService = new FakeDeviceService
        {
            CurrentState = NetworkState.Offline,
            HasDeviceId = false,
            CurrentDevice = null
        };
        deviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.BootstrapNetworkFailure
        });

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        await task.ExecuteOnceAsync();
        Assert.Equal(0, failedStore.ResetAllAbandonedCallCount);

        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        await task.ExecuteOnceAsync();
        await task.ExecuteOnceAsync();
        Assert.Equal(1, failedStore.ResetAllAbandonedCallCount);

        deviceService.SetOffline();
        await task.ExecuteOnceAsync();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        await task.ExecuteOnceAsync();

        Assert.Equal(2, failedStore.ResetAllAbandonedCallCount);
    }

    [Theory]
    [InlineData(0, 20, 40)]
    [InlineData(5, 240, 360)]
    [InlineData(10, 1500, 2100)]
    public async Task RetryFailure_ShouldUseExpectedBackoffWindow(int currentRetryCount, int minSeconds, int maxSeconds)
    {
        CellDataTypeRegistry.Register<StackingCellData>("Stacking");

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        const long recordIdBase = 100;
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = recordIdBase + currentRetryCount,
            Channel = "Cloud",
            ProcessType = "Stacking",
            CellDataJson = SerializeCellData(new StackingCellData
            {
                Barcode = "BC-TEST"
            }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = currentRetryCount,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "http_failure"));

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        var recordId = recordIdBase + currentRetryCount;
        Assert.True(failedStore.Updates.TryGetValue(recordId, out var update));
        Assert.Equal(currentRetryCount + 1, update!.RetryCount);

        var deltaSeconds = (update.NextRetryTime - before).TotalSeconds;
        Assert.InRange(deltaSeconds, minSeconds, maxSeconds);
    }

    [Fact]
    public async Task RetryFailure_WhenExceedMaxRetry_ShouldStopWithMaxValue()
    {
        CellDataTypeRegistry.Register<StackingCellData>("Stacking");

        var logger = new FakeLogService();
        var failedStore = new FakeFailedRecordStore();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        const long recordId = 999;
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = recordId,
            Channel = "Cloud",
            ProcessType = "Stacking",
            CellDataJson = SerializeCellData(new StackingCellData { Barcode = "BC-MAX" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 20,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow
        });

        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "http_failure"));

        var task = new TestableCloudRetryTask(
            logger,
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        await task.ExecuteOnceAsync();

        Assert.True(failedStore.Updates.TryGetValue(recordId, out var update));
        Assert.Equal(21, update!.RetryCount);
        Assert.Equal(DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), update.NextRetryTime);
        Assert.Equal(DateTimeKind.Utc, update.NextRetryTime.Kind);
    }

    [Fact]
    public async Task CloudChannel_ShouldCleanupExpiredAbandonedRecordsOnlyOncePerUtcDay()
    {
        var failedStore = new FakeFailedRecordStore();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask());

        await task.ExecuteOnceAsync();
        await task.ExecuteOnceAsync();

        Assert.Equal(1, failedStore.DeleteExpiredAbandonedCallCount);
        Assert.NotNull(failedStore.LastDeleteExpiredOlderThanUtc);
        Assert.Equal(DateTimeKind.Utc, failedStore.LastDeleteExpiredOlderThanUtc!.Value.Kind);
    }

    [Fact]
    public async Task RetryFailure_ShouldMoveCloudRuntimeStateToBackoff()
    {
        CellDataTypeRegistry.Register<StackingCellData>("Stacking");

        var diagnosticsStore = new FakeCloudDiagnosticsStore();
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1001,
            Channel = "Cloud",
            ProcessType = "Stacking",
            CellDataJson = SerializeCellData(new StackingCellData { Barcode = "BC-BACKOFF" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1)
        });

        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "http_failure"));

        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            diagnosticsStore);

        await task.ExecuteOnceAsync();

        Assert.Equal(CloudRetryRuntimeState.Backoff, diagnosticsStore.Snapshot.RuntimeState);
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

    private static string SerializeCellData(CellDataBase cellData)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(cellData, cellData.GetType(), jsonOptions);
    }

    private sealed class TestableCloudRetryTask
    {
        private readonly CloudRetryTask _inner;

        public TestableCloudRetryTask(
            FakeLogService logger,
            FakeFailedRecordStore failedStore,
            FakeCloudFallbackBufferStore fallbackStore,
            FakeDeviceService deviceService,
            FakeCloudConsumer cloudConsumer,
            FakeCloudBatchConsumer cloudBatchConsumer,
            FakeDeviceLogSyncTask deviceLogSync,
            FakeCapacitySyncTask capacitySync,
            FakeCloudDiagnosticsStore? diagnosticsStore = null,
            FakeCloudDeadLetterStore? deadLetterStore = null,
            FakeCriticalPersistenceFallbackWriter? criticalWriter = null)
        {
            fallbackStore.RetryStore = failedStore;
            _inner = new CloudRetryTask(
                logger,
                failedStore,
                fallbackStore,
                deadLetterStore ?? new FakeCloudDeadLetterStore(),
                criticalWriter ?? new FakeCriticalPersistenceFallbackWriter(),
                deviceService,
                cloudConsumer,
                cloudBatchConsumer,
                deviceLogSync,
                capacitySync,
                diagnosticsStore ?? new FakeCloudDiagnosticsStore());
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();
    }
}
