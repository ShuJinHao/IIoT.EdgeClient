using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
﻿using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Infrastructure.Integration.Mes;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http;
using System.Text;

namespace IIoT.Edge.Mes.ContractTests;

public sealed class MesFrameworkBehaviorTests
{
    [Fact]
    public async Task MesConsumer_WhenLegacyRetryReconstructionCarriesOnlyProcessType_ShouldContinueV2Upload()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());
        var reconstructed = new CellCompletedRecord
        {
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellData = new TestProcessCellData
            {
                Barcode = "MES-LEGACY-RETRY",
                WorkOrderNo = "MES-WO-LEGACY-RETRY",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(
            reconstructed,
            TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        Assert.Equal("Success", diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey)!.LastResult);
    }

    [Fact]
    public async Task MesConsumer_WhenV3ExclusiveIdentityIsPartial_ShouldFailClosedBeforeUploader()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());
        var partialV3 = new CellCompletedRecord
        {
            ClientCode = "CLIENT-MES",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            CellData = new TestProcessCellData
            {
                Barcode = "MES-PARTIAL-V3",
                WorkOrderNo = "MES-WO-PARTIAL-V3",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(
            partialV3,
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(0, uploader.UploadCallCount);
        Assert.Equal(
            "mes_v3_identity_incomplete",
            diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey)!.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_V3_ShouldExposeOnlyNarrowPluginIdentity()
    {
        var uploader = new FakeMesUploaderV3(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var runtime = new StubDevicePluginRuntimeContext(new DevicePluginRuntimeIdentity(
            3,
            "generation-v3",
            "CLIENT-MES-V3",
            TestProcessCellData.ProcessTypeKey,
            "TestModule",
            "2.0.21",
            new string('a', 64)));
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService(),
            runtime);
        var record = new CellCompletedRecord
        {
            ClientCode = "CLIENT-MES-V3",
            CompletionId = "completion-v3",
            TypeKey = "fixture-completion",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            ModuleId = "TestModule",
            CellData = new TestProcessCellData
            {
                Barcode = "MES-V3",
                WorkOrderNo = "MES-WO-V3",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(
            record,
            TestContext.Current.CancellationToken);

        Assert.True(success);
        var context = Assert.IsType<DevicePluginUploadContext>(
            uploader.LastUploadContext);
        Assert.Equal("CLIENT-MES-V3", context.Identity.ClientCode);
        Assert.Equal("TestModule", context.Identity.ModuleId);
        Assert.Equal(TestProcessCellData.ProcessTypeKey, context.Identity.ProcessType);
        Assert.Equal(
            ["ClientCode", "ModuleId", "NormalizedClientCode", "ProcessType"],
            context.Identity.GetType().GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task MesConsumer_V3_ShouldNeverFallbackToLegacyOnlyUploader()
    {
        var legacyUploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var runtime = new StubDevicePluginRuntimeContext(new DevicePluginRuntimeIdentity(
            3,
            "generation-v3",
            "CLIENT-MES-V3",
            TestProcessCellData.ProcessTypeKey,
            "TestModule",
            "2.0.21",
            new string('b', 64)));
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            Array.Empty<IProcessMesUploaderV3>(),
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService(),
            runtime,
            [legacyUploader]);
        var record = new CellCompletedRecord
        {
            ClientCode = "CLIENT-MES-V3",
            CompletionId = "completion-v3",
            TypeKey = "fixture-completion",
            ProcessType = TestProcessCellData.ProcessTypeKey,
            ModuleId = "TestModule",
            CellData = new TestProcessCellData
            {
                Barcode = "MES-V3-LEGACY-ONLY",
                WorkOrderNo = "MES-WO-V3-LEGACY-ONLY",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(
            record,
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(0, legacyUploader.UploadCallCount);
        Assert.Equal(
            "mes_v3_uploader_required",
            diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey)!.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenNoUploaderIsRegistered_ShouldRecordFailureForRetry()
    {
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            uploaders: [],
            new FakeProcessIntegrationRegistry([]),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("uploader_not_registered", diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenUploaderSucceeds_ShouldRecordSuccess()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var logger = new FakeLogService();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            logger);

        var success = await consumer.ProcessAsync(
            CreateTraceableRecord("MES-BC-SUCCESS"),
            TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.NotNull(diagnostics.LastSuccessAt);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == "Info"
            && entry.Message.Contains("[CorrelationId=", StringComparison.Ordinal)
            && entry.Message.Contains("[TaskKey=TestModule.Realtime]", StringComparison.Ordinal)
            && entry.Message.Contains("[BusinessId=MES-BC-SUCCESS]", StringComparison.Ordinal)
            && entry.Message.Contains("结果=Uploaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MesConsumer_WhenRecordCarriesPlcContext_ShouldNotCopyCloudIdentity()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());
        var record = new CellCompletedRecord
        {
            DeviceName = "PLC-RECORD-01",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestProcessCellData
            {
                Barcode = "MES-PLC-CONTEXT-01",
                WorkOrderNo = "MES-WO-PLC",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(record, TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var uploadContext = Assert.IsType<ProcessUploadContext>(uploader.LastUploadContext);
        Assert.Equal("PLC-RECORD-01", uploadContext.Device.DeviceName);
        Assert.Equal(Guid.Empty, uploadContext.Device.DeviceId);
        Assert.Equal(string.Empty, uploadContext.Device.ClientCode);
    }

    [Fact]
    public async Task MesConsumer_WhenRecordTaskIsEquipmentStatus_ShouldRecordDeviceStatusScenario()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());
        var record = new CellCompletedRecord
        {
            DeviceName = "PLC-HM-01",
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.EquipmentStatus",
            CellData = new TestProcessCellData
            {
                Barcode = "MES-EQ-01",
                WorkOrderNo = "MES-WO-01",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(record, TestContext.Current.CancellationToken);

        Assert.True(success);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.Equal("PLC-HM-01", diagnostics.DeviceName);
        Assert.Equal("TestPlugin", diagnostics.ModuleId);
        Assert.Equal("TestPlugin.EquipmentStatus", diagnostics.TaskKey);
        Assert.Equal("设备状态上传", diagnostics.Scenario);
    }

    [Fact]
    public async Task MesConsumer_WhenRecordTargetsCloudOnly_ShouldSkipMesUploader()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(new CellCompletedRecord
        {
            CellData = new TestProcessCellData
            {
                Barcode = "MES-SKIP-CLOUD-ONLY",
                WorkOrderNo = "MES-WO-01",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(0, uploader.UploadCallCount);
        Assert.Null(diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey));
    }

    [Fact]
    public async Task MesConsumer_WhenUploaderReturnsDisabled_ShouldRecordBlockedForRetry()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        uploader.EnqueueResult(MesCallResult.Disabled("可选 MES 场景实时数据未配置，已跳过。"));
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Blocked", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Equal("mes_uploader_disabled", diagnostics.LastBlockedReason);
        Assert.NotNull(diagnostics.LastBlockedAt);
    }

    [Fact]
    public async Task MesConsumer_WhenUploaderReturnsInvalidContext_ShouldReturnFalseForRetry()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        uploader.EnqueueResult(MesCallResult.InvalidContext("token=secret-sensitive-value"));
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var logger = new FakeLogService();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            logger);

        var success = await consumer.ProcessAsync(
            CreateTraceableRecord("MES-BC-FAIL"),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("mes_upload_InvalidContext", diagnostics.LastFailureReason);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == "Error"
            && entry.Message.Contains("[BusinessId=MES-BC-FAIL]", StringComparison.Ordinal)
            && entry.Message.Contains("原因码=mes_upload_InvalidContext", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("secret-sensitive-value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MesConsumer_WhenRegistryHasUploaderButDiDoesNot_ShouldReturnFalse()
    {
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            uploaders: [],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Failed", diagnostics!.LastResult);
        Assert.Equal("uploader_not_found", diagnostics.LastFailureReason);
    }

    [Fact]
    public async Task MesConsumer_WhenMesUploadDisabled_ShouldRecordBlockedForRetryWithoutCallingUploader()
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
            CreateReadyMesGate(runtimeConfig),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(0, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Blocked", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Equal("mes_upload_disabled", diagnostics.LastBlockedReason);
        Assert.NotNull(diagnostics.LastBlockedAt);
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatIsNotReady_ShouldRecordBlockedWithoutCallingUploader()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkNotReady(ExternalSystemKind.Mes, "mes_heartbeat_timeout");

        var consumer = new MesConsumer(
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.Equal(0, uploader.UploadCallCount);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Blocked", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Equal("mes_heartbeat_timeout", diagnostics.LastBlockedReason);
        Assert.NotNull(diagnostics.LastBlockedAt);
    }

    [Fact]
    public async Task MesConsumer_WhenCloudDeviceSessionIsMissing_ShouldStillUpload()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var consumer = new MesConsumer(
            CreateReadyMesGate(),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());
        var record = new CellCompletedRecord
        {
            DeviceName = "PLC-MES-BLOCKED",
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.Realtime",
            CellData = new TestProcessCellData
            {
                Barcode = "MES-NO-DEVICE",
                WorkOrderNo = "MES-WO-NO-DEVICE",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

        var success = await consumer.ProcessAsync(record, TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.Equal(1, uploader.UploadCallCount);
        var uploadContext = Assert.IsType<ProcessUploadContext>(uploader.LastUploadContext);
        Assert.Equal("PLC-MES-BLOCKED", uploadContext.Device.DeviceName);
        Assert.Equal(Guid.Empty, uploadContext.Device.DeviceId);
        Assert.Equal(string.Empty, uploadContext.Device.ClientCode);
        var diagnostics = diagnosticsStore.Get(TestProcessCellData.ProcessTypeKey);
        Assert.NotNull(diagnostics);
        Assert.Equal("Success", diagnostics!.LastResult);
        Assert.Null(diagnostics.LastFailureReason);
        Assert.Null(diagnostics.LastBlockedReason);
        Assert.Equal("PLC-MES-BLOCKED", diagnostics.DeviceName);
        Assert.Equal("TestPlugin", diagnostics.ModuleId);
        Assert.Equal("TestPlugin.Realtime", diagnostics.TaskKey);
        Assert.Equal("生产上传", diagnostics.Scenario);
    }

    [Fact]
    public async Task MesConsumer_WhenHeartbeatRecovers_ShouldAllowUpload()
    {
        var uploader = new FakeMesUploader(TestProcessCellData.ProcessTypeKey);
        var diagnosticsStore = new FakeMesUploadDiagnosticsStore();
        var heartbeatStore = new FakeExternalHeartbeatStateStore();
        heartbeatStore.MarkReady(ExternalSystemKind.Mes);

        var consumer = new MesConsumer(
            CreateMesGate(heartbeatStore: heartbeatStore),
            [uploader],
            CreateMesRegistry(),
            diagnosticsStore,
            new FakeLogService());

        var success = await consumer.ProcessAsync(
            CreateRecord(TestProcessCellData.ProcessTypeKey),
            TestContext.Current.CancellationToken);

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
            "TestPlugin",
            "/api/mes/outbound",
            new { barcode = "MES-01" },
            new Dictionary<string, string>
            {
                ["X-Request"] = "MES"
            },
            TestContext.Current.CancellationToken);

        Assert.True(success);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://mes.test/api/mes/outbound", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("default", handler.LastRequest.Headers.GetValues("X-Default").Single());
        Assert.Equal("MES", handler.LastRequest.Headers.GetValues("X-Request").Single());
    }

    [Fact]
    public async Task MesHttpClient_WhenInFlightSendIsCallerCanceled_ShouldPropagateOriginalCancellation()
    {
        using var handler = new BlockingCancellationHandler();
        using var httpClient = new HttpClient(handler);
        var client = new MesHttpClient(
            new FakeHttpClientFactory(httpClient),
            new FakeMesEndpointProvider(),
            new FakeLogService());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var send = client.PostAsync(
            "TestPlugin",
            "/api/mes/outbound",
            new { barcode = "MES-CANCEL" },
            cancellationToken: cancellation.Token);
        await handler.RequestStarted.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(1, handler.SendCount);
    }

    [Fact]
    public async Task MesHttpClient_WhenTransportSelfCancelsWithoutCallerCancellation_ShouldReturnFailure()
    {
        using var handler = new CaptureHandler
        {
            ExceptionToThrow = new OperationCanceledException("transport self-timeout")
        };
        using var httpClient = new HttpClient(handler);
        var client = new MesHttpClient(
            new FakeHttpClientFactory(httpClient),
            new FakeMesEndpointProvider(),
            new FakeLogService());

        var success = await client.PostAsync(
            "TestPlugin",
            "/api/mes/outbound",
            new { barcode = "MES-TIMEOUT" },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(success);
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task MesEndpointProvider_WhenModuleMesUrlExists_ShouldBuildProcessUrl()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("https://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        var url = await provider.BuildUrlAsync(
            "TestPlugin",
            "/api/mes/outbound",
            TestContext.Current.CancellationToken);

        Assert.True(await provider.IsConfiguredAsync(
            "TestPlugin",
            TestContext.Current.CancellationToken));
        Assert.Equal("https://local-mes.test/api/mes/outbound", url);
    }

    [Fact]
    public async Task MesEndpointProvider_WhenAbsoluteHttpUrlProvided_ShouldKeepUrl()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("https://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        var url = await provider.BuildUrlAsync(
            "TestPlugin",
            "https://override-mes.test/api/mes/outbound",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://override-mes.test/api/mes/outbound", url);
    }

    [Fact]
    public async Task MesEndpointProvider_WhenBaseUrlIsNotHttp_ShouldReportNotConfigured()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider("ftp://local-mes.test"),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        Assert.False(await provider.IsConfiguredAsync(
            "TestPlugin",
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.BuildUrlAsync(
                "TestPlugin",
                "/api/mes/outbound",
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MesEndpointProvider_WhenModuleMesUrlMissing_ShouldReportNotConfigured()
    {
        var provider = new MesEndpointProvider(
            new FakeModuleParamRoleProvider(null),
            new TestOptionsMonitor<MesApiConfig>(new MesApiConfig()));

        Assert.False(await provider.IsConfiguredAsync(
            "TestPlugin",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenEndpointReturnsOk_ShouldMarkReadyAndUseGet()
    {
        using var handler = new CaptureHandler();
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(new HttpClient(handler)),
            new FakeMesEndpointProvider(),
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", "/heath"),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync(TestContext.Current.CancellationToken);

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
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", null),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsReady);
        Assert.Equal("mes_heartbeat_path_missing", snapshot.ReasonCode);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task MesHeartbeatProbe_WhenRequestTimesOut_ShouldReturnNotReady()
    {
        using var handler = new CaptureHandler
        {
            ExceptionToThrow = new OperationCanceledException("deterministic transport timeout")
        };
        using var httpClient = new HttpClient(handler);
        var probe = new MesHeartbeatProbe(
            new FakeHttpClientFactory(httpClient),
            new FakeMesEndpointProvider(),
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeModuleParamRoleProvider("https://local-mes.test", "/heath"),
            new FakeLogService());

        var snapshot = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsReady);
        Assert.Equal("mes_heartbeat_timeout", snapshot.ReasonCode);
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

    private static CellCompletedRecord CreateTraceableRecord(string barcode)
        => new()
        {
            PlcCode = "PLC-MES-01",
            NetworkDeviceId = 1,
            DeviceName = "MES 现场 PLC",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestProcessCellData
            {
                Barcode = barcode,
                WorkOrderNo = "MES-WO-TRACE",
                DeviceCode = "PLC-MES-01",
                DeviceName = "MES 现场 PLC",
                PlcDeviceId = 1,
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        };

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

    private sealed class StubDevicePluginRuntimeContext(
        DevicePluginRuntimeIdentity current) : IDevicePluginRuntimeContext
    {
        public DevicePluginRuntimeIdentity Current { get; } = current;
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
        public Exception? ExceptionToThrow { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            await Task.CompletedTask;
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class BlockingCancellationHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sendCount;

        public Task RequestStarted => _requestStarted.Task;
        public int SendCount => Volatile.Read(ref _sendCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            _requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
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
