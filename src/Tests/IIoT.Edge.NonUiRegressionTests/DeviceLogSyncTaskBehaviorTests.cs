using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Infrastructure.Integration.DeviceLog;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DeviceLogSyncTaskBehaviorTests
{
    [Fact]
    public async Task RetryBuffer_WhenOffline_ShouldSkipPostAndReturnFalse()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var endpointProvider = new FakeCloudApiEndpointProvider();
        var deviceService = new FakeDeviceService
        {
            CurrentState = IIoT.Edge.Application.Abstractions.Device.NetworkState.Offline,
            HasDeviceId = false,
            CurrentDevice = null
        };
        var bufferStore = new FakeDeviceLogBufferStore();
        bufferStore.Records.Add(new DeviceLogRecord
        {
            Id = 1,
            Level = "Info",
            Message = "offline-buffer",
            LogTime = DateTime.UtcNow.ToString("O"),
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        var task = new DeviceLogSyncTask(
            cloudHttp,
            endpointProvider,
            deviceService,
            CreateRuntimeConfig(),
            bufferStore,
            new FakeLogService(),
            new FakeCloudDiagnosticsStore());

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Empty(bufferStore.DeletedClaimTokens);
        Assert.Empty(bufferStore.ReleasedClaimTokens);
    }

    [Fact]
    public async Task RetryBuffer_WhenOnline_ShouldPostAndDeleteClaimedRows()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new IIoT.Edge.Application.Abstractions.Device.DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
                ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var bufferStore = new FakeDeviceLogBufferStore();
        bufferStore.Records.Add(new DeviceLogRecord
        {
            Id = 1,
            Level = "Warn",
            Message = "buffer-1",
            LogTime = DateTime.UtcNow.ToString("O"),
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        var task = new DeviceLogSyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            CreateRuntimeConfig(),
            bufferStore,
            new FakeLogService(),
            new FakeCloudDiagnosticsStore());

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.DeletedClaimTokens);
        Assert.Empty(bufferStore.ReleasedClaimTokens);
        Assert.Empty(bufferStore.Records);
    }

    [Fact]
    public async Task RetryBuffer_WhenPostFails_ShouldReleaseClaimAndKeepRecords()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(false);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new IIoT.Edge.Application.Abstractions.Device.DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
                ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var bufferStore = new FakeDeviceLogBufferStore();
        bufferStore.Records.Add(new DeviceLogRecord
        {
            Id = 1,
            Level = "Error",
            Message = "buffer-keep",
            LogTime = DateTime.UtcNow.ToString("O"),
            CreatedAt = DateTime.UtcNow.ToString("O")
        });

        var task = new DeviceLogSyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            CreateRuntimeConfig(),
            bufferStore,
            new FakeLogService(),
            new FakeCloudDiagnosticsStore());

        var result = await task.RetryBufferAsync();

        Assert.False(result);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Empty(bufferStore.DeletedClaimTokens);
        Assert.Single(bufferStore.ReleasedClaimTokens);
        Assert.Single(bufferStore.Records);
    }

    [Fact]
    public async Task RetryBuffer_WhenBufferedLogsAreOlderThan24Hours_ShouldStillPostAndDeleteClaimedRows()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new IIoT.Edge.Application.Abstractions.Device.DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var oldTime = DateTime.UtcNow.AddHours(-25).ToString("O");
        var bufferStore = new FakeDeviceLogBufferStore();
        bufferStore.Records.Add(new DeviceLogRecord
        {
            Id = 10,
            Level = "Info",
            Message = "stale-buffer",
            LogTime = oldTime,
            CreatedAt = oldTime
        });

        var task = new DeviceLogSyncTask(
            cloudHttp,
            new FakeCloudApiEndpointProvider(),
            deviceService,
            CreateRuntimeConfig(),
            bufferStore,
            new FakeLogService(),
            new FakeCloudDiagnosticsStore());

        var result = await task.RetryBufferAsync();

        Assert.True(result);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Single(bufferStore.DeletedClaimTokens);
        Assert.Empty(bufferStore.Records);
    }

    [Fact]
    public async Task StopAsync_ShouldFlushQueuedLogsToBuffer()
    {
        var logger = new FakeLogService();
        var bufferStore = new FakeDeviceLogBufferStore();
        var task = new DeviceLogSyncTask(
            new FakeCloudHttpClient(),
            new FakeCloudApiEndpointProvider(),
            new FakeDeviceService(),
            CreateRuntimeConfig(),
            bufferStore,
            logger,
            new FakeCloudDiagnosticsStore());

        using var cts = new CancellationTokenSource();
        await task.StartAsync(cts.Token);
        logger.Info("queued-log");

        await task.StopAsync();

        Assert.True(bufferStore.Records.Count >= 1);
        Assert.Contains(bufferStore.Records, x => x.Message == "queued-log");
    }

    [Fact]
    public async Task StopAsync_WhenBufferSaveFails_ShouldRetainQueuedLogsInOrder()
    {
        var logger = new FakeLogService();
        var bufferStore = new FakeDeviceLogBufferStore
        {
            SaveBatchException = new InvalidOperationException("db locked")
        };
        var task = new DeviceLogSyncTask(
            new FakeCloudHttpClient(),
            new FakeCloudApiEndpointProvider(),
            new FakeDeviceService(),
            CreateRuntimeConfig(),
            bufferStore,
            logger,
            new FakeCloudDiagnosticsStore());

        using var cts = new CancellationTokenSource();
        await task.StartAsync(cts.Token);
        logger.Info("first-log");
        logger.Info("second-log");

        await task.StopAsync();

        Assert.Empty(bufferStore.Records);

        bufferStore.SaveBatchException = null;
        await task.StartAsync(CancellationToken.None);
        await task.StopAsync();

        Assert.Equal(
            ["first-log", "second-log"],
            bufferStore.Records
                .Where(x => x.Message.EndsWith("-log", StringComparison.Ordinal))
                .Select(x => x.Message)
                .ToArray());
    }

    [Fact]
    public async Task StartAsync_WhenCloudSyncIntervalConfigured_ShouldUseConfiguredLoopInterval()
    {
        var cloudHttp = new FakeCloudHttpClient();
        cloudHttp.EnqueuePostResult(true);

        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new IIoT.Edge.Application.Abstractions.Device.DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "CLIENT-01",
            ProcessId = Guid.NewGuid()
        });

        var logger = new FakeLogService();
        var task = new DeviceLogSyncTask(
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
            new FakeDeviceLogBufferStore(),
            logger,
            new FakeCloudDiagnosticsStore());

        using var cts = new CancellationTokenSource();
        await task.StartAsync(cts.Token);
        logger.Info("interval-log");
        await WaitForAsync(() => cloudHttp.PostCallCount >= 1);
        await task.StopAsync();

        Assert.True(cloudHttp.PostCallCount >= 1);
    }

    private static FakeLocalSystemRuntimeConfigService CreateRuntimeConfig()
        => new()
        {
            Current = SystemRuntimeConfigSnapshot.Default
        };

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
