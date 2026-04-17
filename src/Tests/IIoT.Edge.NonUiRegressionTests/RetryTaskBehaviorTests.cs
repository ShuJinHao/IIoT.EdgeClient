using IIoT.Edge.Runtime.DataPipeline.Tasks;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Text.Json;
using DeviceSession = IIoT.Edge.Application.Abstractions.Device.DeviceSession;
using NetworkState = IIoT.Edge.Application.Abstractions.Device.NetworkState;

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

        var task = new TestableRetryTask(
            channel: "Cloud",
            logger,
            failedStore,
            deviceService,
            consumers: []);

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
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

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

        var recordId = 100 + currentRetryCount;
        failedStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = recordId,
            Channel = "Cloud",
            ProcessType = "Injection",
            CellDataJson = SerializeCellData(new InjectionCellData
            {
                Barcode = "BC-TEST",
                WorkOrderNo = "WO-TEST"
            }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = currentRetryCount,
            NextRetryTime = DateTime.Now.AddMinutes(-1),
            CreatedAt = DateTime.Now.AddMinutes(-2)
        });

        var consumer = new FakeCellDataConsumer(
            name: "Cloud",
            order: 1,
            retryChannel: "Cloud",
            result: false);

        var task = new TestableRetryTask(
            channel: "Cloud",
            logger,
            failedStore,
            deviceService,
            consumers: [consumer]);

        var before = DateTime.Now;
        await task.ExecuteOnceAsync();

        Assert.True(failedStore.Updates.TryGetValue(recordId, out var update));
        Assert.Equal(currentRetryCount + 1, update!.RetryCount);

        var deltaSeconds = (update.NextRetryTime - before).TotalSeconds;
        Assert.InRange(deltaSeconds, minSeconds, maxSeconds);
    }

    [Fact]
    public async Task RetryFailure_WhenExceedMaxRetry_ShouldStopWithMaxValue()
    {
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

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
            ProcessType = "Injection",
            CellDataJson = SerializeCellData(new InjectionCellData { Barcode = "BC-MAX" }),
            FailedTarget = "Cloud",
            ErrorMessage = "seed",
            RetryCount = 20,
            NextRetryTime = DateTime.Now.AddMinutes(-1),
            CreatedAt = DateTime.Now
        });

        var task = new TestableRetryTask(
            channel: "Cloud",
            logger,
            failedStore,
            deviceService,
            consumers: [new FakeCellDataConsumer("Cloud", 1, "Cloud", result: false)]);

        await task.ExecuteOnceAsync();

        Assert.True(failedStore.Updates.TryGetValue(recordId, out var update));
        Assert.Equal(21, update!.RetryCount);
        Assert.Equal(DateTime.MaxValue, update.NextRetryTime);
    }

    private static string SerializeCellData(CellDataBase cellData)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(cellData, cellData.GetType(), jsonOptions);
    }

    private sealed class TestableRetryTask(
        string channel,
        FakeLogService logger,
        FakeFailedRecordStore failedStore,
        FakeDeviceService deviceService,
        IEnumerable<FakeCellDataConsumer> consumers)
        : RetryTask(channel, logger, failedStore, deviceService, consumers)
    {
        public Task ExecuteOnceAsync() => base.ExecuteAsync();
    }
}
