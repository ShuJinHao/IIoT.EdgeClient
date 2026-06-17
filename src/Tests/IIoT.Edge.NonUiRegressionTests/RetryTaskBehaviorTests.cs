using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class RetryTaskBehaviorTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(5, 30)]
    [InlineData(6, 300)]
    [InlineData(10, 300)]
    [InlineData(11, 1800)]
    public void DefaultRetryBackoffStrategy_ShouldPreserveRetryBoundaries(int retryCount, int expectedSeconds)
    {
        var strategy = new DefaultRetryBackoffStrategy();

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), strategy.Calculate(retryCount));
    }

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
            ProcessType = "OtherProcess",
            CellDataJson = SerializeCellData(new TestCellData
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
    public async Task RetryFailure_ShouldUseInjectedBackoffStrategy()
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

        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 901,
            Channel = "Cloud",
            ProcessType = "OtherProcess",
            CellDataJson = SerializeCellData(new TestCellData { Barcode = "BC-INJECT" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 2,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var cloudConsumer = new FakeCloudConsumer();
        cloudConsumer.EnqueueResult(CloudCallResult.Failure(CloudCallOutcome.HttpFailure, "http_failure"));
        var strategy = new FixedRetryBackoffStrategy(TimeSpan.FromSeconds(7));
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            deviceService,
            cloudConsumer,
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            retryBackoffStrategy: strategy);

        var before = DateTime.UtcNow;
        await task.ExecuteOnceAsync();

        Assert.Equal(1, strategy.CallCount);
        Assert.Equal(3, strategy.LastRetryCount);
        Assert.True(failedStore.Updates.TryGetValue(901, out var update));
        Assert.InRange((update!.NextRetryTime - before).TotalSeconds, 6, 9);
    }

    [Fact]
    public async Task RetryDeserializeFailure_ShouldUseInjectedDeadLetterWriter()
    {
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 902,
            Channel = "Cloud",
            ProcessType = "OtherProcess",
            CellDataJson = "{invalid-json",
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 0,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        });

        var deadLetterWriter = new SpyDataPipelineDeadLetterWriter();
        var task = new TestableCloudRetryTask(
            new FakeLogService(),
            failedStore,
            new FakeCloudFallbackBufferStore(),
            CreateOnlineDeviceService(),
            new FakeCloudConsumer(),
            new FakeCloudBatchConsumer(),
            new FakeDeviceLogSyncTask(),
            new FakeCapacitySyncTask(),
            deadLetterWriter: deadLetterWriter);

        await task.ExecuteOnceAsync();

        Assert.Equal(1, deadLetterWriter.CallCount);
        Assert.Equal("OtherProcess", deadLetterWriter.LastProcessType);
        Assert.Equal("failed_cloud_records", deadLetterWriter.LastSourceTable);
        Assert.Equal(902, deadLetterWriter.LastSourceRecordId);
        Assert.Equal(DeadLetterStage.RetryDeserialize, deadLetterWriter.LastStage);
        Assert.Contains(902, failedStore.DeletedIds);
    }

    [Fact]
    public async Task RetryFailure_WhenExceedMaxRetry_ShouldStopWithMaxValue()
    {
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
            ProcessType = "OtherProcess",
            CellDataJson = SerializeCellData(new TestCellData { Barcode = "BC-MAX" }),
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
        var diagnosticsStore = new FakeCloudDiagnosticsStore();
        var failedStore = new FakeFailedRecordStore();
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1001,
            Channel = "Cloud",
            ProcessType = "OtherProcess",
            CellDataJson = SerializeCellData(new TestCellData { Barcode = "BC-BACKOFF" }),
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
            FakeCriticalPersistenceFallbackWriter? criticalWriter = null,
            IRetryBackoffStrategy? retryBackoffStrategy = null,
            IDataPipelineDeadLetterWriter? deadLetterWriter = null)
        {
            fallbackStore.RetryStore = failedStore;
            var cloudDiagnosticsStore = diagnosticsStore ?? new FakeCloudDiagnosticsStore();
            var cloudDeadLetterStore = deadLetterStore ?? new FakeCloudDeadLetterStore();
            var fallbackWriter = criticalWriter ?? new FakeCriticalPersistenceFallbackWriter();
            var capacityGuard = CreateCapacityGuard(
                logger,
                failedStore,
                new FakeFailedRecordStore(),
                fallbackStore,
                new FakeMesFallbackBufferStore(),
                cloudDiagnosticsStore,
                new FakeMesRetryDiagnosticsStore());
            var backoffStrategy = retryBackoffStrategy ?? new DefaultRetryBackoffStrategy();
            var cloudDeadLetterWriter = deadLetterWriter ?? new DataPipelineDeadLetterWriter();
            var cellDataJsonSerializer = CreateCellDataJsonSerializer();

            _inner = new CloudRetryTask(
                logger,
                deviceService,
                deviceLogSync,
                capacitySync,
                cloudDiagnosticsStore,
                capacityGuard,
                new CloudFallbackRecoveryService(
                    logger,
                    fallbackStore,
                    cloudDeadLetterStore,
                    fallbackWriter,
                    capacityGuard,
                    cloudDeadLetterWriter,
                    cellDataJsonSerializer),
                new CloudRetryRecordProcessor(
                    logger,
                    failedStore,
                    cloudDeadLetterStore,
                    fallbackWriter,
                    cloudConsumer,
                    cloudBatchConsumer,
                    cloudDiagnosticsStore,
                    backoffStrategy,
                    cloudDeadLetterWriter,
                    new DefaultDataPipelineConsumerInvoker(),
                    cellDataJsonSerializer),
                new CloudRetryHousekeepingService(
                    logger,
                    failedStore,
                    cloudDiagnosticsStore));
        }

        public Task ExecuteOnceAsync()
            => _inner.ExecuteOneIterationAsync();
    }

    private static ICellDataJsonSerializer CreateCellDataJsonSerializer()
    {
        var typeRegistry = new CellDataTypeRegistry();
        typeRegistry.Register<TestCellData>("OtherProcess");
        typeRegistry.Register<TestProcessCellData>(TestProcessCellData.ProcessTypeKey);
        return new CellDataJsonSerializer(typeRegistry);
    }

    private sealed class FixedRetryBackoffStrategy(TimeSpan delay) : IRetryBackoffStrategy
    {
        public int CallCount { get; private set; }

        public int LastRetryCount { get; private set; }

        public TimeSpan Calculate(int retryCount)
        {
            CallCount++;
            LastRetryCount = retryCount;
            return delay;
        }
    }

    private sealed class SpyDataPipelineDeadLetterWriter : IDataPipelineDeadLetterWriter
    {
        public int CallCount { get; private set; }
        public string? LastProcessType { get; private set; }
        public string? LastSourceTable { get; private set; }
        public long LastSourceRecordId { get; private set; }
        public DeadLetterStage LastStage { get; private set; }

        public Task<bool> TryPersistAsync(
            Func<DeadLetterRecord, Task> saveAsync,
            ICriticalPersistenceFallbackWriter criticalFallbackWriter,
            ILogService logger,
            DataPipelineDeadLetterChannel channel,
            string processType,
            string cellDataJson,
            string failedTarget,
            string sourceTable,
            long sourceRecordId,
            DeadLetterStage stage,
            string failureReason)
        {
            CallCount++;
            LastProcessType = processType;
            LastSourceTable = sourceTable;
            LastSourceRecordId = sourceRecordId;
            LastStage = stage;
            return Task.FromResult(true);
        }
    }

    private static DataPipelineCapacityGuard CreateCapacityGuard(
        FakeLogService logger,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudFallbackBufferStore cloudFallbackStore,
        IMesFallbackBufferStore mesFallbackStore,
        FakeCloudDiagnosticsStore cloudDiagnosticsStore,
        FakeMesRetryDiagnosticsStore mesDiagnosticsStore)
        => new(
            Options.Create(new DataPipelineCapacityOptions()),
            cloudRetryStore,
            mesRetryStore,
            cloudFallbackStore,
            mesFallbackStore,
            cloudDiagnosticsStore,
            mesDiagnosticsStore,
            logger);
}
