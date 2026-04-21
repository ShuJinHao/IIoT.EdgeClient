using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Text;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class MesFrameworkBehaviorTests
{
    [Fact]
    public async Task MesConsumer_WhenNoUploaderIsRegistered_ShouldSkipRecord()
    {
        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            new FakeLocalSystemRuntimeConfigService(),
            uploaders: [],
            new FakeMesUploadDiagnosticsStore(),
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
    }

    [Fact]
    public async Task MesConsumer_WhenCloudGateIsBlocked_ShouldIgnoreCloudGateAndUpload()
    {
        var uploader = new FakeMesUploader("Injection");
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var deviceService = CreateOnlineDeviceService();
        deviceService.MarkUploadGateBlocked(EdgeUploadBlockReason.UploadTokenRejected, DateTimeOffset.UtcNow);

        var consumer = new MesConsumer(
            deviceService,
            new FakeLocalSystemRuntimeConfigService(),
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get("Injection");
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenUploaderSucceeds_ShouldRecordSuccess()
    {
        var uploader = new FakeMesUploader("Injection");
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            new FakeLocalSystemRuntimeConfigService(),
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get("Injection");
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.NotNull(diagnostics.LastSuccessAt);
        Assert.Null(diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenMesUploadDisabled_ShouldReturnTrueWithoutCallingUploader()
    {
        var uploader = new FakeMesUploader("Injection");
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var runtimeConfig = new FakeLocalSystemRuntimeConfigService
        {
            Current = SystemRuntimeConfigSnapshot.Default with
            {
                MesUploadEnabled = false
            }
        };
        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            runtimeConfig,
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
        Assert.Equal(0, uploader.UploadCallCount);
        Assert.Null(diagnosticsStore.Get("Injection"));
    }

    [Fact]
    public async Task MesHttpClient_ShouldUseEndpointProviderAndMergeHeaders()
    {
        using var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler);
        var endpointProvider = new FakeMesEndpointProvider();
        var client = new MesHttpClient(
            new FakeHttpClientFactory(httpClient),
            endpointProvider,
            new FakeLogService());

        var success = await client.PostAsync(
            "/api/mes/outbound",
            new { barcode = "MES-01" },
            new Dictionary<string, string>
            {
                ["X-Request"] = "MES"
            });

        Assert.True(success);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://mes.test/api/mes/outbound", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("default", handler.LastRequest.Headers.GetValues("X-Default").Single());
        Assert.Equal("MES", handler.LastRequest.Headers.GetValues("X-Request").Single());
    }

    [Fact]
    public void MesEndpointProvider_WhenLocalMesUrlExists_ShouldPreferRuntimeConfig()
    {
        var provider = new MesEndpointProvider(
            new TestOptionsMonitor<MesApiConfig>(
                new MesApiConfig
                {
                    BaseUrl = "https://options-mes.test"
                }),
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    MesBaseUrl = "https://local-mes.test"
                }
            });

        var url = provider.BuildUrl("/api/mes/outbound");

        Assert.True(provider.IsConfigured);
        Assert.Equal("https://local-mes.test/api/mes/outbound", url);
    }

    [Fact]
    public void MesEndpointProvider_WhenLocalMesUrlMissing_ShouldFallbackToOptionsConfig()
    {
        var provider = new MesEndpointProvider(
            new TestOptionsMonitor<MesApiConfig>(
                new MesApiConfig
                {
                    BaseUrl = "https://options-mes.test"
                }),
            new FakeLocalSystemRuntimeConfigService());

        var url = provider.BuildUrl("/api/mes/outbound");

        Assert.True(provider.IsConfigured);
        Assert.Equal("https://options-mes.test/api/mes/outbound", url);
    }

    private static FakeDeviceService CreateOnlineDeviceService()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-MES",
            ClientCode = "CLIENT-MES",
            ProcessId = Guid.NewGuid()
        });
        return deviceService;
    }

    private static CellCompletedRecord CreateRecord(string processType)
    {
        CellDataTypeRegistry.Register<InjectionCellData>("Injection");

        return new CellCompletedRecord
        {
            CellData = new InjectionCellData
            {
                Barcode = "MES-BC-01",
                WorkOrderNo = "MES-WO-01"
            }
        };
    }

    private sealed class FakeMesEndpointProvider : IMesEndpointProvider
    {
        public bool IsConfigured => true;

        public string BuildUrl(string relativeOrAbsoluteUrl)
            => $"https://mes.test{relativeOrAbsoluteUrl}";

        public IReadOnlyDictionary<string, string> GetDefaultHeaders()
            => new Dictionary<string, string>
            {
                ["X-Default"] = "default"
            };
    }

    private sealed class FakeHttpClientFactory(HttpClient httpClient) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => httpClient;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; private set; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener) => NoOpDisposable.Instance;
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
