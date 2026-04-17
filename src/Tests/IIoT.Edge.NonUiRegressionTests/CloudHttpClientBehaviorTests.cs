using IIoT.Edge.Infrastructure.Integration.Http;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CloudHttpClientBehaviorTests
{
    [Fact]
    public async Task PostAsync_WhenResponseIsNotSuccessful_ShouldLogStatusAndReturnFalse()
    {
        var logger = new FakeLogService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = "Bad Gateway"
            }),
            new FakeCloudAccessTokenProvider("jwt-token"),
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.PostAsync("/api/v1/edge/pass-stations/injection/batch", new { barcode = "BC-001" });

        Assert.False(result);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Status=502", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenRequestThrows_ShouldLogExceptionAndReturnNull()
    {
        var logger = new FakeLogService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ => throw new HttpRequestException("network down")),
            new FakeCloudAccessTokenProvider("jwt-token"),
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.GetAsync("/api/v1/edge/capacity/summary");

        Assert.Null(result);
        Assert.Contains(logger.Entries, x => x.Message.Contains("GET exception", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, x => x.Message.Contains("network down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_WhenRequestTargetsBootstrap_ShouldNotAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            new FakeCloudAccessTokenProvider("jwt-token"),
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.GetAsync("/api/v1/edge/bootstrap/device-instance?clientCode=LINE-01");

        Assert.Equal("{}", result);
        Assert.Null(authHeader);
    }

    [Fact]
    public async Task PostAsync_WhenRequestTargetsEdgeLogin_ShouldNotAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            new FakeCloudAccessTokenProvider("jwt-token"),
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync("/api/v1/human/identity/edge-login", new { employeeNo = "E001" });

        Assert.True(result);
        Assert.Null(authHeader);
    }

    [Fact]
    public async Task PostAsync_WhenProtectedRequestHasToken_ShouldAttachBearerHeader()
    {
        AuthenticationHeaderValue? authHeader = null;
        var client = new CloudHttpClient(
            new StubHttpClientFactory(request =>
            {
                authHeader = request.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }),
            new FakeCloudAccessTokenProvider("jwt-token"),
            new FakeCloudApiEndpointProvider(),
            new FakeLogService());

        var result = await client.PostAsync("/api/v1/edge/device-logs", new { deviceId = Guid.NewGuid() });

        Assert.True(result);
        Assert.NotNull(authHeader);
        Assert.Equal("Bearer", authHeader!.Scheme);
        Assert.Equal("jwt-token", authHeader.Parameter);
    }

    [Fact]
    public async Task GetAsync_WhenProtectedRequestHasNoToken_ShouldSkipRequestAndReturnNull()
    {
        var sendCount = 0;
        var logger = new FakeLogService();
        var client = new CloudHttpClient(
            new StubHttpClientFactory(_ =>
            {
                sendCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}")
                };
            }),
            new FakeCloudAccessTokenProvider(),
            new FakeCloudApiEndpointProvider(),
            logger);

        var result = await client.GetAsync("/api/v1/edge/recipes/device/00000000-0000-0000-0000-000000000001");

        Assert.Null(result);
        Assert.Equal(0, sendCount);
        Assert.Contains(logger.Entries, x => x.Message.Contains("Waiting for edge-login", StringComparison.Ordinal));
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubMessageHandler(handlerFactory));
    }

    private sealed class StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handlerFactory(request));
    }
}
