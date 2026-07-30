using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Module.Contracts.Identity;
using System.Text.Json;
using DeviceSession = IIoT.Edge.Module.Contracts.Device.DeviceSession;
using NetworkState = IIoT.Edge.Module.Contracts.Device.NetworkState;

namespace IIoT.Edge.Runtime.WorkflowTests;

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
        Assert.Equal([null, null], cloudHttp.PostIdempotencyKeys);
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
        Assert.Contains(logger.Entries, x => x.Message.Contains("[云端补传] 产能补传失败", StringComparison.Ordinal));
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
    public async Task RetryBuffer_WhenCalledConcurrently_ShouldSerializeCloudPosts()
    {
        var postStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudHttp = new FakeCloudHttpClient
        {
            PostStarted = postStarted,
            PostWait = releasePost.Task
        };
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
            Hour = 8,
            MinuteBucket = 30,
            ShiftCode = "D",
            Total = 13,
            OkCount = 13,
            NgCount = 0,
            PlcName = "PLC-A"
        });
        var task = CreateTask(cloudHttp, deviceService, bufferStore, new FakeLogService());

        var firstRetry = task.RetryBufferAsync();
        await postStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(3),
            TestContext.Current.CancellationToken);
        var secondRetry = task.RetryBufferAsync();

        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal(1, cloudHttp.MaxConcurrentPostCount);

        releasePost.SetResult();
        var results = await Task.WhenAll(firstRetry, secondRetry);

        Assert.All(results, result => Assert.True(result));
        Assert.Equal(2, cloudHttp.PostCallCount);
        Assert.Equal(1, cloudHttp.MaxConcurrentPostCount);
        Assert.Equal(2, bufferStore.DeletedSummaries.Count);
    }

    [Fact]
    public async Task RetryBuffer_WhenMoreThanThreeBatchesRemain_ShouldLeaveRestForNextRound()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        for (var i = 0; i < 601; i++)
        {
            bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
            {
                Date = "2026-04-15",
                Hour = i % 24,
                MinuteBucket = i % 2 == 0 ? 0 : 30,
                ShiftCode = i % 24 is >= 8 and < 20 ? "D" : "N",
                Total = 1,
                OkCount = 1,
                NgCount = 0,
                PlcName = $"PLC-{i:D3}"
            });
        }
        var logger = new FakeLogService();
        var task = CreateTask(cloudHttp, deviceService, bufferStore, logger);

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        Assert.Equal(600, cloudHttp.PostCallCount);
        Assert.Equal([200, 200, 200], bufferStore.ClaimBatchSizes);
        Assert.Equal(600, bufferStore.DeletedSummaries.Count);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("本轮已处理 3 批", StringComparison.Ordinal));
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
        var contextResolution = contextStore.GetOrCreate(
            new PlcIdentity("P1-AP01", 7, "改名后的 AP"));
        Assert.True(contextResolution.IsSuccess);
        var context = contextResolution.Context!;
        context.TodayCapacity.Date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        context.TodayCapacity.DayShift.OkCount = 1;
        context.TodayCapacity.HalfHourly[0].OkCount = 1;
        var diagnostics = new FakeCloudDiagnosticsStore();

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
            diagnostics);

        using var cts = new CancellationTokenSource();
        await task.StartAsync(cts.Token);
        await WaitForAsync(() => cloudHttp.PostCallCount >= 1);
        await task.StopAsync();

        Assert.True(cloudHttp.PostCallCount >= 1);
        var payload = ParsePayload(cloudHttp.PostPayloads[0]);
        Assert.Equal("P1-AP01", payload.GetProperty("plcName").GetString());
        Assert.Equal("P1-AP01", diagnostics.Snapshot.LastPlcCode);
        Assert.Equal("改名后的 AP", diagnostics.Snapshot.LastDeviceName);
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
        static async Task ObserveAsync(Func<bool> observation, CancellationToken cancellationToken)
        {
            while (!observation())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        await ObserveAsync(predicate, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
    }
}
