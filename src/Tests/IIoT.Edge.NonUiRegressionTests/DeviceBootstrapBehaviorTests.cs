using IIoT.Edge.Application.Abstractions.Cloud;
﻿using System.Net;
using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;
using IIoT.Edge.Infrastructure.Integration.Http;

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
    public async Task StartAsync_ShouldBootstrapByClientCodeAndBootstrapSecret()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15);
        var refreshExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7);
        var handler = new RecordingHttpMessageHandler(request =>
        {
            Assert.True(request.Headers.TryGetValues("X-IIoT-Bootstrap-Secret", out var secrets));
            Assert.Equal("secret-LINE-A-01", secrets.Single());

            var response = new HttpResponseMessage(HttpStatusCode.OK)
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
            };
            response.Headers.Add(CloudAuthHeaders.RefreshToken, "refresh-bootstrap-token");
            response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, refreshExpiresAtUtc.ToString("O"));
            response.Headers.Add(CloudAuthHeaders.AccessTokenExpiresAt, expiresAtUtc.ToString("O"));
            return response;
        });

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-A-01", "secret-LINE-A-01"));

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
        Assert.Equal("refresh-bootstrap-token", service.CurrentDevice.RefreshToken);
        Assert.Equal(refreshExpiresAtUtc, service.CurrentDevice.RefreshTokenExpiresAtUtc);
        Assert.True(requestUri.Query.Contains("clientCode=LINE-A-01", StringComparison.Ordinal));
        Assert.False(requestUri.Query.Contains("macAddress=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshBootstrapAsync_WhenRefreshTokenExists_ShouldUseRefreshRouteAndRotateToken()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var refreshRequests = 0;
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/bootstrap/edge-refresh")
            {
                refreshRequests++;
                Assert.False(request.Headers.Contains("X-IIoT-Bootstrap-Secret"));
                Assert.True(request.Headers.TryGetValues(CloudAuthHeaders.RefreshToken, out var refreshTokens));
                Assert.Equal("refresh-1", refreshTokens.Single());

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        Id = deviceId,
                        DeviceName = "Refresh Device",
                        ClientCode = "LINE-R-01",
                        ProcessId = processId,
                        UploadAccessToken = "access-2",
                        UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15)
                    })
                };
                response.Headers.Add(CloudAuthHeaders.RefreshToken, "refresh-2");
                response.Headers.Add(CloudAuthHeaders.RefreshTokenExpiresAt, DateTimeOffset.UtcNow.AddDays(7).ToString("O"));
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-R-01"));

        service.GetType().GetProperty(nameof(DeviceService.CurrentDevice))!.SetValue(service, new DeviceSession
        {
            DeviceId = deviceId,
            DeviceName = "Refresh Device",
            ClientCode = "LINE-R-01",
            ProcessId = processId,
            UploadAccessToken = "access-1",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            RefreshToken = "refresh-1",
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1)
        });

        await service.RefreshBootstrapAsync();

        Assert.Equal(1, refreshRequests);
        Assert.NotNull(service.CurrentDevice);
        Assert.Equal("access-2", service.CurrentDevice!.UploadAccessToken);
        Assert.Equal("refresh-2", service.CurrentDevice.RefreshToken);
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

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-C-03"),
            logger: logger);

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
        Assert.Contains(logger.Entries, x => x.Message.Contains("事件(edge.bootstrap.invalid_token)", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("原因=missing_upload_token", StringComparison.Ordinal));
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

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-D-04"),
            logger: logger);

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
        Assert.Contains(logger.Entries, x => x.Message.Contains("事件(edge.bootstrap.invalid_token)", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("原因=expired_upload_token", StringComparison.Ordinal));
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

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-HB-01"),
            runtimeConfig: new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    OnlineHeartbeatInterval = TimeSpan.FromSeconds(1)
                }
            });

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => Volatile.Read(ref requestCount) >= 2);
        await service.StopAsync();

        Assert.True(Volatile.Read(ref requestCount) >= 2);
    }

    [Fact]
    public async Task StopAsync_WhenHeartbeatDelayIsPending_ShouldCancelLoopBeforeNextBootstrap()
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
                    DeviceName = "Heartbeat Delay Device",
                    ClientCode = "LINE-HB-STOP-01",
                    ProcessId = Guid.NewGuid(),
                    UploadAccessToken = "heartbeat-stop-token",
                    UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
                })
            };
        });

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-HB-STOP-01"),
            runtimeConfig: new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    OnlineHeartbeatInterval = TimeSpan.FromSeconds(1)
                }
            });

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await handler.WaitForRequestUriAsync();
        await WaitForAsync(() => service.CurrentState == NetworkState.Online);

        await service.StopAsync();
        await AssertRequestCountRemainsAsync(
            () => Volatile.Read(ref requestCount),
            expected: 1,
            TimeSpan.FromMilliseconds(1200));

        Assert.Equal(1, Volatile.Read(ref requestCount));
    }

    [Fact]
    public async Task StopAsync_WhenBootstrapRequestIsInFlight_ShouldWaitForHeartbeatExitWithoutFollowUpRequests()
    {
        var requestCount = 0;
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new BlockingHttpMessageHandler(async cancellationToken =>
        {
            Interlocked.Increment(ref requestCount);
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-HB-STOP-02"));

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await AssertRequestCountRemainsAsync(
            () => Volatile.Read(ref requestCount),
            expected: 1,
            TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, Volatile.Read(ref requestCount));
    }

    [Fact]
    public async Task StartAsync_WhenCloudUploadDisabled_ShouldNotCallBootstrap()
    {
        var requestCount = 0;
        var handler = new RecordingHttpMessageHandler(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var service = CreateDeviceService(
            new HttpClient(handler),
            new FakeEndpointProvider("LINE-OFFLINE-01"),
            runtimeConfig: new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    CloudUploadEnabled = false
                }
            });

        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await WaitForAsync(() => service.CurrentUploadGate.Reason == EdgeUploadBlockReason.CloudUploadDisabled);
        await AssertRequestCountRemainsAsync(
            () => Volatile.Read(ref requestCount),
            expected: 0,
            TimeSpan.FromMilliseconds(300));
        await service.StopAsync();

        Assert.Equal(NetworkState.Offline, service.CurrentState);
        Assert.False(service.CanUploadToCloud);
        Assert.Equal(EdgeUploadBlockReason.CloudUploadDisabled, service.CurrentUploadGate.Reason);
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

            await Task.Yield();
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

    private static async Task AssertRequestCountRemainsAsync(
        Func<int> getCount,
        int expected,
        TimeSpan duration)
    {
        var deadline = DateTime.UtcNow.Add(duration);
        while (DateTime.UtcNow < deadline)
        {
            Assert.Equal(expected, getCount());
            await Task.Yield();
        }

        Assert.Equal(expected, getCount());
    }

    private static FakeLocalSystemRuntimeConfigService CreateRuntimeConfig()
        => new()
        {
            Current = SystemRuntimeConfigSnapshot.Default
        };

    private static DeviceService CreateDeviceService(
        HttpClient httpClient,
        ICloudApiEndpointProvider endpointProvider,
        ILocalSystemRuntimeConfigService? runtimeConfig = null,
        FakeLogService? logger = null)
    {
        var logService = logger ?? new FakeLogService();
        return new DeviceService(
            new CloudDeviceBootstrapClient(
                new TestHttpClientFactory(httpClient),
                endpointProvider),
            new DeviceUploadGatePolicy(),
            new DeviceBootstrapEventLogger(logService),
            new DeviceSessionCacheCoordinator(
                new DeviceSessionFileCacheStore(),
                logService),
            runtimeConfig ?? CreateRuntimeConfig(),
            logService);
    }

    private sealed class FakeEndpointProvider(
        string clientCode,
        string bootstrapSecret = "bootstrap-secret") : ICloudApiEndpointProvider
    {
        public string BuildUrl(string relativeOrAbsoluteUrl) => $"https://unit.test{relativeOrAbsoluteUrl}";
        public string GetClientCode() => clientCode;
        public string GetBootstrapSecret() => bootstrapSecret;
        public string GetDeviceInstancePath() => "/api/v1/bootstrap/device-instance";
        public string GetBootstrapRefreshPath() => "/api/v1/bootstrap/edge-refresh";
        public string GetIdentityDeviceLoginPath() => "/api/v1/bootstrap/edge-login";
        public string GetHumanIdentityRefreshPath() => "/api/v1/human/identity/refresh";
        public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
        public string GetProcessUploadPath() => "/api/v1/edge/process-records";
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

    private sealed class BlockingHttpMessageHandler(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => sendAsync(cancellationToken);
    }
}
