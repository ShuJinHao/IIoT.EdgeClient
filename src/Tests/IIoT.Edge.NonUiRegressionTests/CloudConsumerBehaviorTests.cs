using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Application.Common.Http;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Shared;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CloudConsumerBehaviorTests
{
    [Fact]
    public async Task ProcessWithResultAsync_WhenSystemCloudDisabled_ShouldKeepRecordPendingWithoutCallingUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(false),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-DISABLED"
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("cloud_upload_disabled", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, diagnostics.Snapshot.LastOutcome);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudGateBlocked_ShouldReturnRetryableFailureWithoutCallingUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Blocked(
                ExternalSystemKind.Cloud,
                "missing_upload_token",
                "缺少上传令牌。")),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-BLOCKED"
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("missing_upload_token", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, diagnostics.Snapshot.LastOutcome);
        Assert.Equal("missing_upload_token", diagnostics.Snapshot.LastReasonCode);
        Assert.Null(diagnostics.Snapshot.LastFailureAt);
        Assert.NotNull(diagnostics.Snapshot.LastBlockedAt);
        Assert.Equal("缺少上传令牌。", diagnostics.Snapshot.LastBlockedReason);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudRegistered_ShouldUseStandardPassStationUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());
        var completedTime = new DateTime(2026, 6, 5, 8, 30, 0, DateTimeKind.Utc);

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            NetworkDeviceId = 2001,
            DeviceName = "PLC-CLOUD-01",
            ModuleId = "TestPluginAlpha",
            TaskKey = "TestPlugin.RealtimeSampleUpload",
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-STANDARD",
                CellResult = true,
                CompletedTime = completedTime,
                RuntimeStatus = "待上传"
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal("/api/v1/edge/pass-stations/otherprocess/batch", cloudHttp.LastPostUrl);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
        Assert.Equal("PLC-CLOUD-01", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("TestPluginAlpha", diagnostics.Snapshot.LastModuleId);
        Assert.Equal("TestPlugin.RealtimeSampleUpload", diagnostics.Snapshot.LastTaskKey);
        Assert.Equal("生产上传", diagnostics.Snapshot.LastScenario);

        var payload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.LastPayload);
        Assert.Equal("otherprocess", payload.ProcessType);
        Assert.Equal(1, payload.SchemaVersion);
        var item = Assert.Single(payload.Items);
        Assert.Equal("BAR-CLOUD-STANDARD", item.Barcode);
        Assert.Equal("OK", item.CellResult);
        Assert.Equal(completedTime, item.CompletedTime);
        Assert.Equal("BAR-CLOUD-STANDARD", item.Payload.GetProperty("barcode").GetString());
        Assert.Equal("待上传", item.Payload.GetProperty("runtimeStatus").GetString());
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenRecordTargetsMesOnly_ShouldSkipCloudUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            new FakeCloudDiagnosticsStore(),
            new FakeLogService());

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-MES-ONLY",
                UploadTargets = DataPipelineUploadTargets.Mes
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenDeviceStatusTargetsCloud_ShouldSkipStandardPassStationAndRecordBlocked()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var logger = new FakeLogService();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            logger);

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            NetworkDeviceId = 2001,
            DeviceName = "PLC-HM-01",
            ModuleId = "Homogenization",
            TaskKey = "Homogenization.EquipmentStatus",
            CellData = new TestCellData
            {
                Barcode = "STATUS-CLOUD-01",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, diagnostics.Snapshot.LastOutcome);
        Assert.Equal("cloud_plc_device_status_endpoint_missing", diagnostics.Snapshot.LastReasonCode);
        Assert.Equal("PLC-HM-01", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("Homogenization", diagnostics.Snapshot.LastModuleId);
        Assert.Equal("Homogenization.EquipmentStatus", diagnostics.Snapshot.LastTaskKey);
        Assert.Equal("设备状态上传", diagnostics.Snapshot.LastScenario);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("PLC 设备状态专用接口未就绪", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenSameProcessContainsMultiplePlcs_ShouldUploadPerPlcContext()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var registry = new FakeProcessIntegrationRegistry();
        registry.RegisterCloudUploader("OtherProcess", ProcessUploadMode.Batch);
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            registry,
            new FakeCloudDiagnosticsStore(),
            new FakeLogService());

        var plcARecord = new CellCompletedRecord
        {
            NetworkDeviceId = 1001,
            DeviceName = "PLC-A",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestCellData
            {
                Barcode = "BAR-PLC-A"
            }
        };
        var plcBRecord = new CellCompletedRecord
        {
            NetworkDeviceId = 1002,
            DeviceName = "PLC-B",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestCellData
            {
                Barcode = "BAR-PLC-B"
            }
        };

        var result = await consumer.ProcessBatchAsync(
            [plcARecord, plcBRecord],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, cloudHttp.PostCallCount);
        var payloads = cloudHttp.PostPayloads
            .Cast<PassStationBatchUploadPayload>()
            .ToList();
        Assert.All(payloads, payload => Assert.Single(payload.Items));
        Assert.Contains(payloads, payload => payload.Items[0].Barcode == "BAR-PLC-A");
        Assert.Contains(payloads, payload => payload.Items[0].Barcode == "BAR-PLC-B");
        var expectedPlcAKey = CloudIdempotencyKeyBuilder.ForBatch(
            "otherprocess",
            nameof(StandardPassStationCloudUploader),
            [plcARecord]);
        var expectedPlcBKey = CloudIdempotencyKeyBuilder.ForBatch(
            "otherprocess",
            nameof(StandardPassStationCloudUploader),
            [plcBRecord]);
        Assert.Contains(expectedPlcAKey, cloudHttp.PostIdempotencyKeys);
        Assert.Contains(expectedPlcBKey, cloudHttp.PostIdempotencyKeys);
        Assert.NotEqual(expectedPlcAKey, expectedPlcBKey);
        Assert.Contains(payloads, payload => payload.RequestId == expectedPlcAKey);
        Assert.Contains(payloads, payload => payload.RequestId == expectedPlcBKey);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenProductionAndDeviceStatusSharePlc_ShouldUploadOnlyProductionToPassStation()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());
        var productionRecord = new CellCompletedRecord
        {
            NetworkDeviceId = 2001,
            DeviceName = "PLC-HM-01",
            ModuleId = "Homogenization",
            TaskKey = "Homogenization.Realtime",
            CellData = new TestCellData
            {
                Barcode = "PROD-CLOUD-01",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };
        var statusRecord = new CellCompletedRecord
        {
            NetworkDeviceId = 2001,
            DeviceName = "PLC-HM-01",
            ModuleId = "Homogenization",
            TaskKey = "Homogenization.EquipmentStatus",
            CellData = new TestCellData
            {
                Barcode = "STATUS-CLOUD-02",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

        var result = await consumer.ProcessBatchAsync(
            [productionRecord, statusRecord],
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        var payload = Assert.IsType<PassStationBatchUploadPayload>(cloudHttp.LastPayload);
        var item = Assert.Single(payload.Items);
        Assert.Equal("PROD-CLOUD-01", item.Barcode);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
        Assert.Equal("PLC-HM-01", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("Homogenization.Realtime", diagnostics.Snapshot.LastTaskKey);
        Assert.Equal("生产上传", diagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenStandardPassStationPathInvalid_ShouldReturnFailureWithoutCallingHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new ThrowingCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-BAD-PATH"
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.Exception, result.Outcome);
        Assert.Equal("standard_pass_station_upload_exception", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudNotRegistered_ShouldReturnRetryableFailure()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var logger = new FakeLogService();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            new FakeProcessIntegrationRegistry(),
            diagnostics,
            logger);

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            DeviceName = "PLC-CLOUD-MISSING",
            ModuleId = "MissingCloudModule",
            TaskKey = "MissingCloudModule.Realtime",
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-MISSING"
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("cloud_uploader_missing", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, diagnostics.Snapshot.LastOutcome);
        Assert.Equal("cloud_uploader_missing", diagnostics.Snapshot.LastReasonCode);
        Assert.Equal("PLC-CLOUD-MISSING", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("MissingCloudModule", diagnostics.Snapshot.LastModuleId);
        Assert.Equal("MissingCloudModule.Realtime", diagnostics.Snapshot.LastTaskKey);
        Assert.NotNull(diagnostics.Snapshot.LastBlockedAt);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("未注册 Cloud 上传器", StringComparison.Ordinal));
    }

    private static FakeDeviceService CreateOnlineDeviceService()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-CLOUD",
            ClientCode = "CLIENT-CLOUD",
            ProcessId = Guid.NewGuid()
        });
        return deviceService;
    }

    private static FakeProcessIntegrationRegistry CreateCloudRegistry()
    {
        var registry = new FakeProcessIntegrationRegistry();
        registry.RegisterCloudUploader("OtherProcess", ProcessUploadMode.Single);
        return registry;
    }

    private sealed class FixedCloudUploadGate(UploadGateSnapshot snapshot) : ICloudUploadGate
    {
        public ExternalSystemKind System => ExternalSystemKind.Cloud;

        public UploadGateSnapshot GetSnapshot() => snapshot;
    }

    private sealed class FixedCloudExecutionPolicy(bool isEnabled) : ICloudExecutionPolicy
    {
        public bool IsEnabled { get; } = isEnabled;
    }

    private sealed class ThrowingCloudApiEndpointProvider : ICloudApiPathProvider
    {
        public string GetDeviceLogPath() => "/api/v1/edge/device-logs";
        public string GetProcessUploadPath() => "/api/v1/edge/process-records";

        public string GetPassStationBatchPath(string typeKey)
            => throw new InvalidOperationException("Pass station path is invalid.");

        public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
        public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
        public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
    }

}
