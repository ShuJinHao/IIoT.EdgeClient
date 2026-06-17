using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Shared;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class CloudConsumerBehaviorTests
{
    [Fact]
    public async Task ProcessAsync_WhenCloudUploadDisabled_ShouldSkipUploaderAndReturnSuccess()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudUploadGate(UploadGateSnapshot.Blocked(
                ExternalSystemKind.Cloud,
                "cloud_upload_disabled",
                "云端上传已关闭。")),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());

        var result = await consumer.ProcessAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-DISABLED"
            }
        });

        Assert.True(result);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal(CloudCallOutcome.Success, diagnostics.Snapshot.LastOutcome);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudGateBlocked_ShouldReturnRetryableFailureWithoutCallingUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
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
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("missing_upload_token", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudRegistered_ShouldUseStandardPassStationUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            new FakeLogService());
        var completedTime = new DateTime(2026, 6, 5, 8, 30, 0, DateTimeKind.Utc);

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-STANDARD",
                CellResult = true,
                CompletedTime = completedTime,
                RuntimeStatus = "待上传"
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal("/api/v1/edge/pass-stations/otherprocess/batch", cloudHttp.LastPostUrl);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);

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
    public async Task ProcessWithResultAsync_WhenStandardPassStationPathInvalid_ShouldReturnFailureWithoutCallingHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
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
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.Exception, result.Outcome);
        Assert.Equal("standard_pass_station_upload_exception", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
    }

    [Fact]
    public async Task ProcessWithResultAsync_WhenCloudNotRegistered_ShouldSkipProcess()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            new FakeProcessIntegrationRegistry(),
            new FakeCloudDiagnosticsStore(),
            new FakeLogService());

        var result = await consumer.ProcessWithResultAsync(new CellCompletedRecord
        {
            CellData = new TestCellData
            {
                Barcode = "BAR-CLOUD-SKIP"
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, cloudHttp.PostCallCount);
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
