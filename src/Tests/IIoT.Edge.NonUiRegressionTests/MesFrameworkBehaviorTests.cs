using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
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
            CreateReadyMesGate(),
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
            CreateReadyMesGate(),
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
            CreateReadyMesGate(),
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
            CreateReadyMesGate(runtimeConfig),
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
        Assert.Equal(0, uploader.UploadCallCount);
        Assert.Null(diagnosticsStore.Get("Injection"));
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatIsNotReady_ShouldReturnFalseWithoutCallingUploader()
    {
        var uploader = new FakeMesUploader("Injection");
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");

        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.False(success);
        Assert.Equal(0, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get("Injection");
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("mes_heartbeat_timeout", diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatRecovers_ShouldAllowUpload()
    {
        var uploader = new FakeMesUploader("Injection");
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);

        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord("Injection"));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        Assert.Equal("Success", diagnosticsStore.Get("Injection")!.LastResult);
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

    [Fact]
    public async Task MesHeartbeatProbe_WhenEndpointReturnsOk_ShouldMarkReadyAndUseGet()
    {
        using var handler = new CaptureHandler();
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(new HttpClient(handler)),
            new FakeMesEndpointProvider(),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig { HeartbeatPath = "/api/mes/heartbeat" }),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync();

        Assert.True(snapshot.IsReady);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://mes.test/api/mes/heartbeat", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenHeartbeatPathMissing_ShouldReturnNotReadyWithoutRequest()
    {
        using var handler = new CaptureHandler();
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(new HttpClient(handler)),
            new FakeMesEndpointProvider(),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig { HeartbeatPath = "" }),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync();

        Assert.False(snapshot.IsReady);
        Assert.Equal("mes_heartbeat_path_missing", snapshot.ReasonCode);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenRequestTimesOut_ShouldReturnNotReady()
    {
        using var handler = new CaptureHandler { Delay = TimeSpan.FromSeconds(5) };
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(httpClient),
            new FakeMesEndpointProvider(),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig { HeartbeatPath = "/api/mes/heartbeat" }),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync();

        Assert.False(snapshot.IsReady);
        Assert.Equal("mes_heartbeat_timeout", snapshot.ReasonCode);
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

    private static MesUploadGate CreateReadyMesGate(FakeLocalSystemRuntimeConfigService? runtimeConfig = null)
    {
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);
        return CreateMesGate(runtimeConfig, heartbeatStore);
    }

    private static MesUploadGate CreateMesGate(
        FakeLocalSystemRuntimeConfigService? runtimeConfig = null,
        FakeExternalHeartbeatStateStore? heartbeatStore = null)
        => new(
            runtimeConfig ?? new FakeLocalSystemRuntimeConfigService(),
            heartbeatStore ?? new FakeExternalHeartbeatStateStore());

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
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public TimeSpan? Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (Delay.HasValue)
            {
                await Task.Delay(Delay.Value, cancellationToken);
            }

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
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
