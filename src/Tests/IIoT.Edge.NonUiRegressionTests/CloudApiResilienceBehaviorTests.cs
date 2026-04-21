using IIoT.Edge.Infrastructure.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CloudApiResilienceBehaviorTests
{
    [Fact]
    public async Task CloudApiClient_WhenGetReturnsTransientFailure_ShouldRetry()
    {
        using var handler = new SequenceMessageHandler(
        [
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
        ]);

        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("CloudApi");

        using var response = await client.GetAsync("https://example.test/api/v1/edge/capacity/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.SendCount);
    }

    [Fact]
    public async Task CloudApiClient_WhenPostReturnsTransientFailure_ShouldNotRetryUnsafeMethod()
    {
        using var handler = new SequenceMessageHandler(
        [
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
        ]);

        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("CloudApi");

        using var response = await client.PostAsync(
            "https://example.test/api/v1/edge/device-logs",
            JsonContent.Create(new { deviceId = Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(1, handler.SendCount);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationInfrastructure(configuration, Path.GetTempPath());
        services.AddHttpClient("CloudApi")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class SequenceMessageHandler(IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responses)
        : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            var responseFactory = _responses.Count > 0
                ? _responses.Dequeue()
                : _ => new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(responseFactory(request));
        }
    }
}
