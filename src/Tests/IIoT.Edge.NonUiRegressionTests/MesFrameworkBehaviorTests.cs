using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
﻿using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Infrastructure.Integration.Mes;
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
            new FakeProcessIntegrationRegistry([]),
            new FakeMesUploadDiagnosticsStore(),
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.True(success);
    }

    [Fact]
    public async Task MesConsumer_WhenCloudGateIsBlocked_ShouldIgnoreCloudGateAndUpload()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var deviceService = CreateOnlineDeviceService();
        deviceService.MarkUploadGateBlocked(EdgeUploadBlockReason.UploadTokenRejected, DateTimeOffset.UtcNow);

        var consumer = new MesConsumer(
            deviceService,
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenUploaderSucceeds_ShouldRecordSuccess()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.NotNull(diagnostics.LastSuccessAt);
        Assert.Null(diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenRegistryHasUploaderButDiDoesNot_ShouldReturnFalse()
    {
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateReadyMesGate(),
            uploaders: [],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.False(success);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("uploader_not_found", diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenMesUploadDisabled_ShouldReturnTrueWithoutCallingUploader()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
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
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.True(success);
        Assert.Equal(0, uploader.UploadCallCount);
        Assert.Null(diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey));
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatIsNotReady_ShouldReturnFalseWithoutCallingUploader()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");

        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.False(success);
        Assert.Equal(0, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("mes_heartbeat_timeout", diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatRecovers_ShouldAllowUpload()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);

        var consumer = new MesConsumer(
            CreateOnlineDeviceService(),
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(CreateRecord(TestProcessCellData.ProcessTypeKey));

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        Assert.Equal("Success", diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey)!.LastResult);
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
            "Homogenization",
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
    public async Task MesEndpointProvider_WhenModuleMesUrlExists_ShouldBuildProcessUrl()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("https://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        var url = await provider.BuildUrlAsync("Homogenization", "/api/mes/outbound");

        Assert.True(await provider.IsConfiguredAsync("Homogenization"));
        Assert.Equal("https://local-mes.test/api/mes/outbound", url);
    }

    [Fact]
    public async Task MesEndpointProvider_WhenAbsoluteHttpUrlProvided_ShouldKeepUrl()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("https://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        var url = await provider.BuildUrlAsync("Homogenization", "https://override-mes.test/api/mes/outbound");

        Assert.Equal("https://override-mes.test/api/mes/outbound", url);
    }

    [Fact]
    public async Task MesEndpointProvider_WhenBaseUrlIsNotHttp_ShouldReportNotConfigured()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("ftp://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        Assert.False(await provider.IsConfiguredAsync("Homogenization"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.BuildUrlAsync("Homogenization", "/api/mes/outbound"));
    }

    [Fact]
    public async Task MesEndpointProvider_WhenModuleMesUrlMissing_ShouldReportNotConfigured()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider(null),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        Assert.False(await provider.IsConfiguredAsync("Homogenization"));
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenEndpointReturnsOk_ShouldMarkReadyAndUseGet()
    {
        using var handler = new CaptureHandler();
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(new HttpClient(handler)),
            new FakeMesEndpointProvider(),
            new FakeProcessIntegrationRegistry(["Homogenization"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", "/heath"),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync();

        Assert.True(snapshot.IsReady);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("https://mes.test/heath", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenHeartbeatPathMissing_ShouldReturnNotReadyWithoutRequest()
    {
        using var handler = new CaptureHandler();
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(new HttpClient(handler)),
            new FakeMesEndpointProvider(),
            new FakeProcessIntegrationRegistry(["Homogenization"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", null),
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
            new FakeProcessIntegrationRegistry(["Homogenization"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", "/heath"),
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

    private static FakeProcessIntegrationRegistry CreateMesRegistry()
        => new([TestProcessCellData.ProcessTypeKey]);

    private static CellCompletedRecord CreateRecord(string processType)
    {

        return new CellCompletedRecord
        {
            CellData = new TestProcessCellData
            {
                Barcode = "MES-BC-01",
                WorkOrderNo = "MES-WO-01"
            }
        };
    }

    private sealed class FakeMesEndpointProvider : IMesEndpointProvider
    {
        public Task<bool> IsConfiguredAsync(string processType, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string> BuildUrlAsync(
            string processType,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"https://mes.test{relativeOrAbsoluteUrl}");

        public Task<string?> TryBuildFirstConfiguredUrlAsync(
            IReadOnlyCollection<string> processTypes,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>($"https://mes.test{relativeOrAbsoluteUrl}");

        public IReadOnlyDictionary<string, string> GetDefaultHeaders()
            => new Dictionary<string, string>
            {
                ["X-Default"] = "default"
            };
    }

    private sealed class FakeModuleParamRoleProvider(string? mesBaseUrl, string? mesHealthPath = "/heath") : IModuleParamRoleProvider
    {
        public Task<ModuleParamRoleValue?> GetAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ModuleParamRoleValue?>(null);

        public Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModuleParamRoleValue>>([]);

        public Task<string?> GetStringAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            string? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role switch
            {
                ModuleParamRole.MesBaseUrl => mesBaseUrl,
                ModuleParamRole.MesHealthPath => mesHealthPath,
                _ => defaultValue
            });

        public Task<string?> FirstStringAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role switch
            {
                ModuleParamRole.MesBaseUrl => mesBaseUrl,
                ModuleParamRole.MesHealthPath => mesHealthPath,
                _ => null
            });

        public Task<bool> GetBoolAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);

        public Task<bool> AnyBoolAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);
    }

    private sealed class FakeProcessIntegrationRegistry(IEnumerable<string> mesProcessTypes) : IProcessIntegrationRegistry
    {
        private readonly Dictionary<string, ProcessUploaderRegistration> _mesUploaders = mesProcessTypes
            .ToDictionary(
                static processType => processType,
                static processType => new ProcessUploaderRegistration(processType, ProcessUploadMode.Single),
                StringComparer.OrdinalIgnoreCase);

        public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
            => throw new NotSupportedException();

        public void RegisterMesUploader(string processType, ProcessUploadMode uploadMode)
            => throw new NotSupportedException();

        public bool HasCloudUploader(string processType) => false;

        public bool HasMesUploader(string processType) => _mesUploaders.ContainsKey(processType);

        public bool TryGetCloudUploader(string processType, out ProcessUploaderRegistration registration)
        {
            registration = default!;
            return false;
        }

        public bool TryGetMesUploader(string processType, out ProcessUploaderRegistration registration)
            => _mesUploaders.TryGetValue(processType, out registration!);

        public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetCloudUploaders()
            => new Dictionary<string, ProcessUploaderRegistration>();

        public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetMesUploaders()
            => _mesUploaders;
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
