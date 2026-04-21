using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using System.Text.Json;
using DeviceSession = IIoT.Edge.Application.Abstractions.Device.DeviceSession;
using NetworkState = IIoT.Edge.Application.Abstractions.Device.NetworkState;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CapacitySyncTaskBehaviorTests
{
    [Fact]
    public async Task RetryBuffer_WhenOnlineAndAllPostsSucceed_ShouldPostAllAndDeleteClaimedSummaries()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        var deviceId = Guid.NewGuid();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = deviceId,
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-15",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 12,
            OkCount = 11,
            NgCount = 1,
            PlcName = "PLC-A"
        });
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-15",
            Hour = 23,
            MinuteBucket = 30,
            ShiftCode = "N",
            Total = 5,
            OkCount = 4,
            NgCount = 1,
            PlcName = "PLC-A"
        });

        var task = CreateTask(cloudHttp, deviceService, bufferStore, new FakeLogService());

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        Assert.Equal(2, cloudHttp.PostCallCount);
        Assert.Equal(2, bufferStore.DeletedSummaries.Count);
        Assert.Empty(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.HourlySummaries);

        var payload1 = ParsePayload(cloudHttp.PostPayloads[0]);
        Assert.Equal(deviceId, payload1.GetProperty("deviceId").GetGuid());
        Assert.Equal("08:00-08:30", payload1.GetProperty("timeLabel").GetString());
        Assert.Equal("D", payload1.GetProperty("shiftCode").GetString());
        Assert.Equal(12, payload1.GetProperty("totalCount").GetInt32());
        Assert.Equal("PLC-A", payload1.GetProperty("plcName").GetString());

        var payload2 = ParsePayload(cloudHttp.PostPayloads[1]);
        Assert.Equal("23:30-00:00", payload2.GetProperty("timeLabel").GetString());
        Assert.Equal("N", payload2.GetProperty("shiftCode").GetString());
    }

    [Fact]
    public async Task RetryBuffer_WhenAnyPostFails_ShouldReleaseClaimAndKeepRemainingSummaries()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);
        cloudHttp.EnqueuePostResult(false);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-15",
            Hour = 10,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 7,
            OkCount = 6,
            NgCount = 1,
            PlcName = "PLC-A"
        });
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-15",
            Hour = 10,
            MinuteBucket = 30,
            ShiftCode = "D",
            Total = 8,
            OkCount = 8,
            NgCount = 0,
            PlcName = "PLC-A"
        });

        var logger = new FakeLogService();
        var task = CreateTask(cloudHttp, deviceService, bufferStore, logger);

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(2, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.DeletedSummaries);
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Equal(30, bufferStore.HourlySummaries[0].MinuteBucket);
        Assert.Contains(logger.Entries, x => x.Message.Contains("[Retry-Cloud] Capacity retry failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetryBuffer_WhenOnlineButDeviceMissing_ShouldReturnFalseWithoutPost()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService
        {
            CurrentState = NetworkState.Online,
            HasDeviceId = true,
            CurrentDevice = null
        };

        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-15",
            Hour = 9,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 3,
            OkCount = 3,
            NgCount = 0,
            PlcName = "PLC-A"
        });

        var task = CreateTask(cloudHttp, deviceService, bufferStore, new FakeLogService());

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Empty(bufferStore.DeletedSummaries);
        Assert.Empty(bufferStore.ReleasedClaimTokens);
    }

    [Fact]
    public async Task RetryBuffer_WhenHourlySummaryIsOlderThan24Hours_ShouldStillPostAndDeleteClaimedSummary()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        var deviceId = Guid.NewGuid();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = deviceId,
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = DateTime.UtcNow.AddHours(-25).ToString("yyyy-MM-dd"),
            Hour = 6,
            MinuteBucket = 0,
            ShiftCode = "N",
            Total = 9,
            OkCount = 8,
            NgCount = 1,
            PlcName = "PLC-A"
        });

        var task = CreateTask(cloudHttp, deviceService, bufferStore, new FakeLogService());

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.DeletedSummaries);
        Assert.Empty(bufferStore.HourlySummaries);
    }

    [Fact]
    public async Task StartAsync_WhenCloudSyncIntervalConfigured_ShouldUseConfiguredLoopInterval()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var contextStore = new FakeProductionContextStore();
        var context = contextStore.GetOrCreate("PLC-A");
        context.TodayCapacity.Date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        context.TodayCapacity.DayShift.OkCount = 1;
        context.TodayCapacity.HalfHourly[0].OkCount = 1;

        var task = new CapacitySyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    CloudSyncInterval = TimeSpan.FromSeconds(1)
                }
            },
            contextStore,
            new FakeCapacityBufferStore(),
            new FakeLogService(),
            new ShiftConfig
            {
                DayStart = "08:00",
                DayEnd = "20:00"
            },
            new FakeCloudDiagnosticsStore());

        using var cts = new CancellationTokenSource();
        await task.StartAsync(cts.Token);
        await WaitForAsync(() => cloudHttp.PostCallCount >= 1);
        await task.StopAsync();

        Assert.True(cloudHttp.PostCallCount >= 1);
    }

    private static CapacitySyncTask CreateTask(
        FakeCloudHttpClient cloudHttp,
        FakeDeviceService deviceService,
        FakeCapacityBufferStore bufferStore,
        FakeLogService logger)
    {
        return new CapacitySyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default
            },
            new FakeProductionContextStore(),
            bufferStore,
            logger,
            new ShiftConfig
            {
                DayStart = "08:00",
                DayEnd = "20:00"
            },
            new FakeCloudDiagnosticsStore());
    }

    private static JsonElement ParsePayload(object payload)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return doc.RootElement.Clone();
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }
}
