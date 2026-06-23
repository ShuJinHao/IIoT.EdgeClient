using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Infrastructure.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Timeout;
using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;

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

    [Fact]
    public async Task CloudApiClient_WhenConfiguredTimeoutIsLowerThanResponseTime_ShouldHonorConfiguredTimeout()
    {
        using var handler = new DelayedMessageHandler(TimeSpan.FromSeconds(5));

        using var provider = BuildProvider(
            handler,
            new Dictionary<string, string?>
            {
                ["CloudApi:TimeoutSecs"] = "1"
            });
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("CloudApi");
        var stopwatch = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() => client.PostAsync(
            "https://example.test/api/v1/edge/device-logs",
            JsonContent.Create(new { deviceId = Guid.NewGuid() })));

        stopwatch.Stop();

        Assert.NotNull(exception);
        Assert.True(
            exception is TimeoutRejectedException or TaskCanceledException or OperationCanceledException,
            $"Expected a timeout-related exception but got {exception.GetType().FullName}.");
        Assert.Equal(1, handler.SendCount);
        Assert.InRange(stopwatch.Elapsed.TotalMilliseconds, 700, 5000);
    }

    private static ServiceProvider BuildProvider(
        HttpMessageHandler handler,
        IDictionary<string, string?>? configValues = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-cloud-api-resilience", Guid.NewGuid().ToString("N"));
        var runtimePaths = new EdgeRuntimePaths(
            BaseDirectory: tempDirectory,
            ProfileName: "Test",
            RuntimeDataRoot: tempDirectory,
            DatabaseDirectory: Path.Combine(tempDirectory, "db"),
            ContextDirectory: Path.Combine(tempDirectory, "context"),
            RecipeDirectory: Path.Combine(tempDirectory, "recipe"),
            ExcelDirectory: Path.Combine(tempDirectory, "excel"),
            DiagnosticsDirectory: Path.Combine(tempDirectory, "diagnostics"),
            LogDirectory: Path.Combine(tempDirectory, "diagnostics", "logs"),
            DeviceCacheFilePath: Path.Combine(tempDirectory, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(tempDirectory, "diagnostics", "crash.log"),
            FallbackCrashLogPath: Path.Combine(tempDirectory, "diagnostics", "crash.fallback.log"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIntegrationInfrastructure(configuration, runtimePaths);
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

    private sealed class DelayedMessageHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
