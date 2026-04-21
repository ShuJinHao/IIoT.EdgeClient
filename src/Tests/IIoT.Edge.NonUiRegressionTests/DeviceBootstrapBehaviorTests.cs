using System.Net;
using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DeviceBootstrapBehaviorTests : IDisposable
{
    private readonly string _cacheFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "device_cache.json");

    public DeviceBootstrapBehaviorTests()
    {
        DeleteCacheFile();
    }

    [Fact]
    public async Task StartAsync_ShouldBootstrapByClientCodeOnly()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                Id = deviceId,
                DeviceName = "Test Device",
                ClientCode = "LINE-A-01",
                ProcessId = processId,
                UploadAccessToken = "device-upload-token",
                UploadAccessTokenExpiresAtUtc = expiresAtUtc
            })
        });

        var service = new DeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-A-01"),
            new DeviceSessionFileCacheStore(),
            CreateRuntimeConfig(),
            new FakeLogService());

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        var requestUri = await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => service.CurrentDevice is not null);
        await service.StopAsync();

        Assert.NotNull(service.CurrentDevice);
        Assert.Equal(deviceId, service.CurrentDevice!.DeviceId);
        Assert.Equal(processId, service.CurrentDevice.ProcessId);
        Assert.Equal("LINE-A-01", service.CurrentDevice.ClientCode);
        Assert.Equal("device-upload-token", service.CurrentDevice.UploadAccessToken);
        Assert.Equal(expiresAtUtc, service.CurrentDevice.UploadAccessTokenExpiresAtUtc);
        Assert.True(requestUri.Query.Contains("clientCode=LINE-A-01", StringComparison.Ordinal));
        Assert.False(requestUri.Query.Contains("macAddress=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryLoad_ShouldMapLegacyMacAddressCacheToRequestedClientCode()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();

        File.WriteAllText(
            _cacheFilePath,
            $$"""
            {
              "DeviceId": "{{deviceId}}",
              "DeviceName": "Cached Device",
              "MacAddress": "HW1234567890",
              "ProcessId": "{{processId}}"
            }
            """);

        var store = new DeviceSessionFileCacheStore();

        var session = store.TryLoad("LINE-B-02");

        Assert.NotNull(session);
        Assert.Equal(deviceId, session!.DeviceId);
        Assert.Equal(processId, session.ProcessId);
        Assert.Equal("Cached Device", session.DeviceName);
        Assert.Equal("LINE-B-02", session.ClientCode);

        var migrated = File.ReadAllText(_cacheFilePath);
        Assert.True(migrated.Contains("\"ClientCode\":\"LINE-B-02\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenBootstrapReturnsEmptyUploadToken_ShouldRemainBlocked()
    {
        var logger = new FakeLogService();
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                Id = Guid.NewGuid(),
                DeviceName = "Invalid Device",
                ClientCode = "LINE-C-03",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            })
        });

        var service = new DeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-C-03"),
            new DeviceSessionFileCacheStore(),
            CreateRuntimeConfig(),
            logger);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => service.CurrentUploadGate.Reason == EdgeUploadBlockReason.MissingUploadToken);
        await service.StopAsync();

        Assert.Equal(NetworkState.Offline, service.CurrentState);
        Assert.False(service.CanUploadToCloud);
        Assert.Equal(EdgeUploadGateState.Blocked, service.CurrentUploadGate.State);
        Assert.Equal(EdgeUploadBlockReason.MissingUploadToken, service.CurrentUploadGate.Reason);
        Assert.NotNull(service.CurrentDevice);
        Assert.Contains(logger.Entries, x => x.Message.Contains("event=edge.bootstrap.invalid_token", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("reason=missing_upload_token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenBootstrapReturnsExpiredUploadToken_ShouldRemainBlocked()
    {
        var logger = new FakeLogService();
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                Id = Guid.NewGuid(),
                DeviceName = "Expired Device",
                ClientCode = "LINE-D-04",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "expired-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            })
        });

        var service = new DeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-D-04"),
            new DeviceSessionFileCacheStore(),
            CreateRuntimeConfig(),
            logger);

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => service.CurrentUploadGate.Reason == EdgeUploadBlockReason.ExpiredUploadToken);
        await service.StopAsync();

        Assert.Equal(NetworkState.Offline, service.CurrentState);
        Assert.False(service.CanUploadToCloud);
        Assert.Equal(EdgeUploadGateState.Blocked, service.CurrentUploadGate.State);
        Assert.Equal(EdgeUploadBlockReason.ExpiredUploadToken, service.CurrentUploadGate.Reason);
        Assert.NotNull(service.CurrentDevice);
        Assert.Contains(logger.Entries, x => x.Message.Contains("event=edge.bootstrap.invalid_token", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("reason=expired_upload_token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenHeartbeatIntervalConfigured_ShouldUseConfiguredOnlineInterval()
    {
        var requestCount = 0;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = Guid.NewGuid(),
                    DeviceName = "Heartbeat Device",
                    ClientCode = "LINE-HB-01",
                    ProcessId = Guid.NewGuid(),
                    UploadAccessToken = "heartbeat-token",
                    UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                })
            };
        });

        var service = new DeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-HB-01"),
            new DeviceSessionFileCacheStore(),
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    OnlineHeartbeatInterval = TimeSpan.FromSeconds(1)
                }
            },
            new FakeLogService());

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => Volatile.Read(ref requestCount) >= 2);
        await service.StopAsync();

        Assert.True(Volatile.Read(ref requestCount) >= 2);
    }

    public void Dispose()
    {
        DeleteCacheFile();
    }

    private void DeleteCacheFile()
    {
        if (File.Exists(_cacheFilePath))
        {
            File.Delete(_cacheFilePath);
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

    private static FakeLocalSystemRuntimeConfigService CreateRuntimeConfig()
        => new()
        {
            Current = SystemRuntimeConfigSnapshot.Default
        };

    private sealed class FakeEndpointProvider(string clientCode) : ICloudApiEndpointProvider
    {
        public string BuildUrl(string relativeOrAbsoluteUrl) => $"https://unit.test{relativeOrAbsoluteUrl}";
        public string GetClientCode() => clientCode;
        public string GetDeviceInstancePath() => "/api/v1/edge/bootstrap/device-instance";
        public string GetIdentityDeviceLoginPath() => "/api/v1/human/identity/edge-login";
        public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
        public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
        public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
        public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
        public string BuildRecipeByDevicePath(Guid deviceId) => $"/api/v1/edge/recipes/device/{deviceId}";
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<Uri> _requestUriSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requestUriSource.TrySetResult(request.RequestUri!);
            return Task.FromResult(responseFactory(request));
        }

        public async Task<Uri> WaitForRequestUriAsync()
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var registration = timeoutCts.Token.Register(() => _requestUriSource.TrySetCanceled(timeoutCts.Token));
            return await _requestUriSource.Task;
        }
    }
}
