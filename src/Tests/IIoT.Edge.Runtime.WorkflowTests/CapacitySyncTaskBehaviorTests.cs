using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Infrastructure.Integration.Capacity;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using System.Linq.Expressions;
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
    public async Task RetryBuffer_WhenLegacyDeviceNameWasVerified_ShouldUploadStablePlcCode()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "改名前"
        });
        var contexts = new FakeProductionContextStore();
        contexts.GetOrCreate(new PlcIdentity("P1-AP01", 7, "改名后"));
        var aliases = new InMemoryPlcIdentityAliasRegistry();
        aliases.ObserveVerifiedAlias("P1-AP01", "改名前");
        aliases.ObserveVerifiedAlias("P1-AP01", "改名后");
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            contexts,
            aliases);

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        var payload = ParsePayload(Assert.Single(cloudHttp.PostPayloads));
        Assert.Equal("P1-AP01", payload.GetProperty("plcName").GetString());
        Assert.Empty(bufferStore.HourlySummaries);
    }

    [Fact]
    public async Task RetryBuffer_WhenRuntimeContextIsMissing_ShouldResolveDisabledConfiguredPlcCode()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "P1-AP01"
        });
        var configuredPlc = NetworkDeviceEntity.Create(
            "禁用但已配置",
            DeviceType.PLC,
            "127.0.0.1",
            102,
            "P1-AP01");
        configuredPlc.Disable();
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            new FakeProductionContextStore(),
            new InMemoryPlcIdentityAliasRegistry(),
            new FakeNetworkDeviceReadRepository([configuredPlc]));

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        var payload = ParsePayload(Assert.Single(cloudHttp.PostPayloads));
        Assert.Equal("P1-AP01", payload.GetProperty("plcName").GetString());
        Assert.Empty(bufferStore.HourlySummaries);
    }

    [Fact]
    public async Task RetryBuffer_WhenOnlyCurrentDeviceNameMatches_ShouldFailClosedAndReleaseClaim()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "当前展示名称"
        });
        var configuredPlc = NetworkDeviceEntity.Create(
            "当前展示名称",
            DeviceType.PLC,
            "127.0.0.1",
            102,
            "P1-AP01");
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            new FakeProductionContextStore(),
            new InMemoryPlcIdentityAliasRegistry(),
            new FakeNetworkDeviceReadRepository([configuredPlc]));

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.DeletedSummaries);
    }

    [Fact]
    public async Task RetryBuffer_WhenPlcCodeAlsoExistsAsAnotherPlcAlias_ShouldPreferExactCode()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "P1-AP01"
        });
        var contexts = new FakeProductionContextStore();
        contexts.GetOrCreate(new PlcIdentity("P1-AP01", 7, "一号 PLC"));
        contexts.GetOrCreate(new PlcIdentity("P1-AP02", 8, "二号 PLC"));
        var aliases = new InMemoryPlcIdentityAliasRegistry();
        aliases.ObserveVerifiedAlias("P1-AP02", "P1-AP01");
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            contexts,
            aliases);

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        var payload = ParsePayload(Assert.Single(cloudHttp.PostPayloads));
        Assert.Equal("P1-AP01", payload.GetProperty("plcName").GetString());
        Assert.Empty(bufferStore.HourlySummaries);
    }

    [Fact]
    public async Task RetryBuffer_WhenHistoricalAliasIsReusedByAnotherPlc_ShouldFailClosedAndReleaseClaim()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "被复用的历史名称"
        });
        var contexts = new FakeProductionContextStore();
        contexts.GetOrCreate(new PlcIdentity("P1-AP01", 7, "一号 PLC"));
        contexts.GetOrCreate(new PlcIdentity("P1-AP02", 8, "二号 PLC"));
        var aliases = new InMemoryPlcIdentityAliasRegistry();
        aliases.ObserveVerifiedAlias("P1-AP01", "被复用的历史名称");
        aliases.ObserveVerifiedAlias("P1-AP02", "被复用的历史名称");
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            contexts,
            aliases);

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.DeletedSummaries);
    }

    [Fact]
    public async Task RetryBuffer_WhenLegacyIdentityIsUnresolved_ShouldPreserveOriginalRows()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "无法确认的旧名称"
        });
        var logger = new FakeLogService();
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            logger,
            new FakeProductionContextStore(),
            new InMemoryPlcIdentityAliasRegistry());

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.DeletedSummaries);
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("原记录已保留", StringComparison.Ordinal)
                     && entry.Message.Contains(
                         "未上传、移动或删除",
                         StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetryBuffer_WhenBlockedRowsFillFirstBatch_ShouldContinueWithLaterResolvableRows()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        for (var index = 0; index < 200; index++)
        {
            bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
            {
                Date = "2026-07-30",
                Hour = index % 24,
                MinuteBucket = index % 2 == 0 ? 0 : 30,
                ShiftCode = index % 24 is >= 8 and < 20 ? "D" : "N",
                Total = 1,
                OkCount = 1,
                PlcName = $"无法解析-{index:D3}"
            });
        }

        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-31",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "P1-AP01"
        });
        var contexts = new FakeProductionContextStore();
        contexts.GetOrCreate(new PlcIdentity("P1-AP01", 7, "当前名称"));
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            contexts,
            new InMemoryPlcIdentityAliasRegistry());

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        var payload = ParsePayload(Assert.Single(cloudHttp.PostPayloads));
        Assert.Equal("P1-AP01", payload.GetProperty("plcName").GetString());
        Assert.Equal(200, bufferStore.HourlySummaries.Count);
        Assert.DoesNotContain(bufferStore.HourlySummaries, row => row.PlcName == "P1-AP01");
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Equal([200, 200], bufferStore.ClaimBatchSizes);
    }

    [Fact]
    public async Task RetryBuffer_WhenAuthoritativePlcConfigurationReadFails_ShouldNotClaimOrChangeRows()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Host",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });
        var bufferStore = new FakeCapacityBufferStore();
        bufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-07-30",
            Hour = 8,
            MinuteBucket = 0,
            ShiftCode = "D",
            Total = 2,
            OkCount = 2,
            PlcName = "P1-AP01"
        });
        var task = CreateTask(
            cloudHttp,
            deviceService,
            bufferStore,
            new FakeLogService(),
            new FakeProductionContextStore(),
            new InMemoryPlcIdentityAliasRegistry(),
            new FakeNetworkDeviceReadRepository(
                [],
                new IOException("configured PLC read failed")));

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.HourlySummaries);
        Assert.Empty(bufferStore.ClaimBatchSizes);
        Assert.Empty(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.DeletedSummaries);
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
        FakeLogService logger,
        FakeProductionContextStore? contextStore = null,
        IPlcIdentityAliasRegistry? identityAliasRegistry = null,
        IReadRepository<NetworkDeviceEntity>? networkDevices = null)
    {
        var seedContextsFromBuffer = contextStore is null;
        contextStore ??= new FakeProductionContextStore();
        if (seedContextsFromBuffer)
        {
            var nextDeviceId = 1;
            foreach (var plcCode in bufferStore.HourlySummaries
                         .Select(static summary => summary.PlcName)
                         .Where(static value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                contextStore.GetOrCreate(
                    new PlcIdentity(plcCode, nextDeviceId++, plcCode));
            }
        }

        return new CapacitySyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default
            },
            contextStore,
            bufferStore,
            logger,
            new ShiftConfig
            {
                DayStart = "08:00",
                DayEnd = "20:00"
            },
            new FakeCloudDiagnosticsStore(),
            identityAliasRegistry,
            networkDevices);
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

    private sealed class FakeNetworkDeviceReadRepository(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        Exception? listFailure = null)
        : IReadRepository<NetworkDeviceEntity>
    {
        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => listFailure is null
                ? Task.FromResult(devices.Where(expression.Compile()).ToList())
                : Task.FromException<List<NetworkDeviceEntity>>(listFailure);

        public Task<NetworkDeviceEntity?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            Expression<Func<NetworkDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => GetListAsync(expression, cancellationToken);

        public Task<List<NetworkDeviceEntity>> GetListAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NetworkDeviceEntity?> GetSingleOrDefaultAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<NetworkDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> CountAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<NetworkDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
