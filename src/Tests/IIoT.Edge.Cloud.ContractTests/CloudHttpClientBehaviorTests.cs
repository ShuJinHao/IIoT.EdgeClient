using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration.Http;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Cloud.ContractTests;

public sealed class CloudHttpClientBehaviorTests
{
    [Fact]
    public async Task PostAsync_WhenResponseIsNotSuccessful_ShouldLogStatusAndReturnFalse()
    {
        var logger = new FakeLogService();
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Bad Gateway"
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.PostAsync(
            "/api/v1/edge/pass-stations/test-process/batch",
            new { barcode = "BC-001" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.HttpFailure, result.Outcome);
        Assert.Equal(HttpStatusCode.BadGateway, result.HttpStatusCode);
        Assert.Equal("http_status_502", result.ReasonCode);
        Assert.Contains(logger.Entries, x => x.Message.Contains("状态码=502", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenRequestThrows_ShouldLogNetworkExceptionAndReturnFailure()
    {
        var logger = new FakeLogService();
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => throw new HttpRequestException("network down")),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Payload);
        Assert.Equal(CloudCallOutcome.NetworkFailure, result.Outcome);
        Assert.Equal("network_exception", result.ReasonCode);
        Assert.Contains(logger.Entries, x => x.Message.Contains("GET 网络异常", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("network down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenRequestTargetsBootstrap_ShouldNotAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        Uri? requestUri = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                requestUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync(
            "/api/v1/bootstrap/device-instance?clientCode=LINE-01",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("{}", result.Payload);
        Assert.Equal("https://cloud.test/api/v1/bootstrap/device-instance?clientCode=LINE-01", requestUri!.ToString());
        Assert.Null(authHeader);
    }

    [Fact]
    public async Task PostAsync_WhenRequestTargetsEdgeLogin_ShouldNotAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/bootstrap/edge-login",
            new { employeeNo = "E001" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.Success, result.Outcome);
        Assert.Null(authHeader);
    }

    [Fact]
    public async Task PostAsync_WhenProtectedRequestHasToken_ShouldAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/device-logs",
            new { deviceId = Guid.NewGuid() },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.Success, result.Outcome);
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader!.Scheme);
        Assert.Equal(deviceService.CurrentDevice!.UploadAccessToken, authHeader.Parameter);
    }

    [Fact]
    public async Task PostAsync_WhenPayloadContainsIdentityKeys_ShouldRemoveClientIdentityFields()
    {
        string? requestBody = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(async (request, cancellationToken) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/device-logs",
            new
            {
                deviceId = deviceService.CurrentDevice!.DeviceId,
                clientCode = "LINE-01",
                macAddress = "00-11-22-33",
                level = "INFO"
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(requestBody!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("deviceId", out _));
        Assert.True(root.TryGetProperty("level", out _));
        Assert.False(root.TryGetProperty("clientCode", out _));
        Assert.False(root.TryGetProperty("macAddress", out _));
    }

    [Fact]
    public async Task PostAsync_WhenPayloadContainsNestedIdentityKeys_ShouldRemoveClientIdentityFieldsRecursively()
    {
        string? requestBody = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(async (request, cancellationToken) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/pass-stations/test-process/batch",
            new
            {
                deviceId = deviceService.CurrentDevice!.DeviceId,
                processType = "test-process",
                payload = new
                {
                    clientCode = "LINE-NESTED",
                    mac_address = "AA-BB-CC",
                    keep = "value"
                },
                records = new object[]
                {
                    new { barcode = "B001", client_code = "LINE-ARRAY" },
                    new { barcode = "B002", nested = new { macAddress = "11-22-33", keep = "nested-value" } }
                }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(requestBody!);
        AssertNoClientIdentityKeys(doc.RootElement);
        Assert.True(doc.RootElement.GetProperty("payload").TryGetProperty("keep", out _));
        Assert.Equal("nested-value", doc.RootElement.GetProperty("records")[1].GetProperty("nested").GetProperty("keep").GetString());
    }

    [Fact]
    public async Task PostAsync_WhenPayloadIsPlcRuntimeStateReport_ShouldKeepClientCodeForCloudContract()
    {
        string? requestBody = null;
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(async (request, cancellationToken) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/edge-hosts/plc-runtime-states",
            new EdgeHostPlcRuntimeStateReport(
                deviceService.CurrentDevice!.DeviceId,
                "LINE-01",
                DateTime.UtcNow,
                [
                    new EdgeHostPlcRuntimeStateReportItem("PLC-A01", "PLC-A01", true, "Connected")
                ]),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(requestBody!);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("deviceId", out _));
        Assert.True(root.TryGetProperty("clientCode", out var clientCode));
        Assert.Equal("LINE-01", clientCode.GetString());
        Assert.True(root.TryGetProperty("plcStates", out var plcStates));
        Assert.Single(plcStates.EnumerateArray());
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestHasNoToken_ShouldSkipRequestAndReturnNull()
    {
        var sendCount = 0;
        var logger = new FakeLogService();
        var deviceService = new FakeDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ =>
            {
                sendCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.GetAsync(
            "/api/v1/edge/recipes/device/00000000-0000-0000-0000-000000000001",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Payload);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal(EdgeUploadBlockReason.MissingUploadToken.ToReasonCode(), result.ReasonCode);
        Assert.Equal(0, sendCount);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("event=edge.upload.auth.refresh_before_send", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("event=edge.upload.auth.skip_no_valid_token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestTokenIsExpired_ShouldSkipRequestAndReturnNull()
    {
        var sendCount = 0;
        var logger = new FakeLogService();
        var deviceService = CreateOnlineDeviceService(expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ =>
            {
                sendCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Payload);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal(EdgeUploadBlockReason.ExpiredUploadToken.ToReasonCode(), result.ReasonCode);
        Assert.Equal(0, sendCount);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("reason=expired_upload_token", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("event=edge.upload.auth.skip_no_valid_token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestHasNoTokenAndRefreshSucceeds_ShouldSendRequest()
    {
        AuthenticationHeaderValue? authHeader = null;
        var deviceService = new FakeDeviceService();
        deviceService.RefreshBootstrapHandler = _ =>
        {
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                DeviceName = "Device-A",
                ClientCode = "LINE-01",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "refreshed-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return Task.CompletedTask;
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("{}", result.Payload);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.NotNull(authHeader);
        Assert.Equal("refreshed-token", authHeader!.Parameter);
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestTokenIsExpiredAndRefreshSucceeds_ShouldSendRequestWithFreshToken()
    {
        AuthenticationHeaderValue? authHeader = null;
        var deviceService = CreateOnlineDeviceService(accessToken: "expired-token", expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2));
        deviceService.RefreshBootstrapHandler = _ =>
        {
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = deviceService.CurrentDevice!.DeviceId,
                DeviceName = deviceService.CurrentDevice.DeviceName,
                ClientCode = deviceService.CurrentDevice.ClientCode,
                ProcessId = deviceService.CurrentDevice.ProcessId,
                UploadAccessToken = "fresh-after-refresh",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return Task.CompletedTask;
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("{}", result.Payload);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.NotNull(authHeader);
        Assert.Equal("fresh-after-refresh", authHeader!.Parameter);
    }

    [Fact]
    public async Task PostAsync_WhenProtectedRequestReturns401_ShouldRefreshBootstrapAndRetryOnce()
    {
        var sentTokens = new List<string?>();
        var deviceService = CreateOnlineDeviceService(accessToken: "stale-token");
        deviceService.RefreshBootstrapHandler = _ =>
        {
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = deviceService.CurrentDevice!.DeviceId,
                DeviceName = deviceService.CurrentDevice.DeviceName,
                ClientCode = deviceService.CurrentDevice.ClientCode,
                ProcessId = deviceService.CurrentDevice.ProcessId,
                UploadAccessToken = "fresh-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return Task.CompletedTask;
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                sentTokens.Add(request.Headers.Authorization?.Parameter);
                var isFresh = string.Equals(request.Headers.Authorization?.Parameter, "fresh-token", StringComparison.Ordinal);
                return new HttpResponseMessage(isFresh ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/device-logs",
            new { deviceId = deviceService.CurrentDevice!.DeviceId },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.Success, result.Outcome);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.Equal(["stale-token", "fresh-token"], sentTokens);
        Assert.Contains(sentTokens, token => token == "fresh-token");
    }

    [Fact]
    public async Task PostAsync_WhenIdempotencyKeyProvided_ShouldReuseSameHeaderOnRetry()
    {
        var idempotencyKeys = new List<string?>();
        var deviceService = CreateOnlineDeviceService(accessToken: "stale-token");
        deviceService.RefreshBootstrapHandler = _ =>
        {
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = deviceService.CurrentDevice!.DeviceId,
                DeviceName = deviceService.CurrentDevice.DeviceName,
                ClientCode = deviceService.CurrentDevice.ClientCode,
                ProcessId = deviceService.CurrentDevice.ProcessId,
                UploadAccessToken = "fresh-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return Task.CompletedTask;
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                idempotencyKeys.Add(
                    request.Headers.TryGetValues("X-Idempotency-Key", out var values)
                        ? values.Single()
                        : null);
                var isFresh = string.Equals(request.Headers.Authorization?.Parameter, "fresh-token", StringComparison.Ordinal);
                return new HttpResponseMessage(isFresh ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync(
            "/api/v1/edge/device-logs",
            new { deviceId = deviceService.CurrentDevice!.DeviceId },
            new CloudRequestOptions { IdempotencyKey = "idem-001" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["idem-001", "idem-001"], idempotencyKeys);
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestReturns401AfterRetry_ShouldReturnUnauthorizedAfterRetry()
    {
        var deviceService = CreateOnlineDeviceService(accessToken: "stale-token");
        deviceService.RefreshBootstrapHandler = _ =>
        {
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = deviceService.CurrentDevice!.DeviceId,
                DeviceName = deviceService.CurrentDevice.DeviceName,
                ClientCode = deviceService.CurrentDevice.ClientCode,
                ProcessId = deviceService.CurrentDevice.ProcessId,
                UploadAccessToken = "still-bad-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            return Task.CompletedTask;
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.UnauthorizedAfterRetry, result.Outcome);
        Assert.Equal("unauthorized_after_retry", result.ReasonCode);
        Assert.Equal(HttpStatusCode.Unauthorized, result.HttpStatusCode);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
    }

    [Fact]
    public async Task GetAsync_WhenConcurrentRequestsNeedRefresh_ShouldUseSingleRefreshFlight()
    {
        var sendCount = 0;
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceService = new FakeDeviceService();
        deviceService.RefreshBootstrapHandler = async _ =>
        {
            refreshStarted.TrySetResult();
            await releaseRefresh.Task;
            deviceService.SetOnline(new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                DeviceName = "Device-A",
                ClientCode = "LINE-01",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "single-flight-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
        };

        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                Interlocked.Increment(ref sendCount);
                Assert.Equal("single-flight-token", request.Headers.Authorization?.Parameter);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var first = client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);
        await refreshStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        var second = client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);
        releaseRefresh.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
        Assert.Equal(2, sendCount);
    }

    [Fact]
    public async Task GetAsync_WhenRequestTimesOut_ShouldReturnNetworkFailure()
    {
        var deviceService = CreateOnlineDeviceService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => throw new TaskCanceledException("request timed out")),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.NetworkFailure, result.Outcome);
        Assert.Equal("timeout", result.ReasonCode);
    }

    [Fact]
    public async Task PostAsync_WhenInFlightRequestIsCanceled_ShouldAbortTransportWithoutRefreshRetryOrGateMutation()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendCount = 0;
        var deviceService = CreateOnlineDeviceService();
        var initialGate = deviceService.CurrentUploadGate;
        var client = new CloudHttpClient(
            new StubHttpClientFactory(async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref sendCount);
                requestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var postTask = client.PostAsync(
            "/api/v1/edge/pass-stations/test-process/batch",
            new { barcode = "BC-CANCEL" },
            cancellationToken: cts.Token);
        await requestStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => postTask);
        Assert.Equal(1, sendCount);
        Assert.Equal(0, deviceService.RefreshBootstrapCallCount);
        Assert.Equal(initialGate, deviceService.CurrentUploadGate);
    }

    [Fact]
    public async Task GetAsync_WhenBootstrapRefreshIsCanceled_ShouldAbortBeforeHttpSend()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sendCount = 0;
        var deviceService = new FakeDeviceService
        {
            RefreshBootstrapHandler = async cancellationToken =>
            {
                refreshStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        };
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ =>
            {
                Interlocked.Increment(ref sendCount);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            deviceService,
            deviceService,
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var getTask = client.GetAsync(
            "/api/v1/edge/capacity/summary",
            cancellationToken: cts.Token);
        await refreshStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => getTask);
        Assert.Equal(0, sendCount);
        Assert.Equal(1, deviceService.RefreshBootstrapCallCount);
    }

    private static FakeDeviceService CreateOnlineDeviceService(
        string accessToken = "device-token",
        DateTimeOffset? expiresAtUtc = null)
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Device-A",
            ClientCode = "LINE-01",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = accessToken,
            UploadAccessTokenExpiresAtUtc = expiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(10)
        });
        return deviceService;
    }

    private static void AssertNoClientIdentityKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.DoesNotMatch(
                        "^(clientCode|client_code|macAddress|mac_address)$",
                        property.Name);
                    AssertNoClientIdentityKeys(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoClientIdentityKeys(item);
                }

                break;
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFactory;

        public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory)
            : this((request, _) => Task.FromResult(handlerFactory(request)))
        {
        }

        public StubHttpClientFactory(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFactory)
        {
            _handlerFactory = handlerFactory;
        }

        public HttpClient CreateClient(string name) => new(new StubMessageHandler(_handlerFactory));
    }

    private sealed class StubMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handlerFactory(request, cancellationToken);
    }
}
