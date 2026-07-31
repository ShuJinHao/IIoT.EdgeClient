using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration.PassStation;
using IIoT.Edge.Module.Sdk.Cloud;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Shared;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class CloudConsumerBehaviorTests
{
    [Fact]
    public async Task ProcessWithResultAsync_WhenSystemCloudDisabled_ShouldKeepRecordPendingWithoutCallingUploader()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var diagnostics = new FakeCloudDiagnosticsStore();
        var logger = new FakeLogService();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(false),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            logger);

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
        var logger = new FakeLogService();
        var consumer = new CloudConsumer(
            CreateOnlineDeviceService(),
            new FixedCloudExecutionPolicy(true),
            new FixedCloudUploadGate(UploadGateSnapshot.Ready(ExternalSystemKind.Cloud)),
            new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp),
            CreateCloudRegistry(),
            diagnostics,
            logger);
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
        Assert.Contains(logger.Entries, entry =>
            entry.Level == "Info"
            && entry.Message.Contains("[CorrelationId=", StringComparison.Ordinal)
            && entry.Message.Contains("[TaskKey=TestPlugin.RealtimeSampleUpload]", StringComparison.Ordinal)
            && entry.Message.Contains("[BusinessId=BAR-CLOUD-STANDARD]", StringComparison.Ordinal)
            && entry.Message.Contains("结果=Uploaded", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, true, "pass_station_cell_result_required")]
    [InlineData(true, false, "pass_station_completed_time_required")]
    public async Task ProcessWithResultAsync_WhenCloudRequiredFieldIsMissing_ShouldFailBeforeHttp(
        bool hasCellResult,
        bool hasCompletedTime,
        string expectedReasonCode)
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
                Barcode = "BAR-CLOUD-REQUIRED",
                CellResult = hasCellResult ? true : null,
                CompletedTime = hasCompletedTime ? DateTime.UtcNow : null
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.InvalidPayload, result.Outcome);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Theory]
    [InlineData("1999-12-31T23:59:59Z")]
    [InlineData("2099-01-01T00:00:00Z")]
    public async Task ProcessWithResultAsync_WhenCompletedTimeIsOutsideCloudRange_ShouldFailBeforeHttp(
        string completedTimeText)
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
                Barcode = "BAR-CLOUD-TIME",
                CellResult = true,
                CompletedTime = DateTime.Parse(
                    completedTimeText,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal)
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("pass_station_completed_time_out_of_range", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenBatchContainsInvalidRecord_ShouldRejectWholeBatchBeforeHttp()
    {
        var cloudHttp = new FakeCloudHttpClient();
        var uploader = new StandardPassStationCloudUploader(new FakeCloudApiEndpointProvider(), cloudHttp);
        var device = CreateOnlineDeviceService().CurrentDevice!;

        var result = await uploader.UploadAsync(
            new ProcessUploadContext(device),
            "OtherProcess",
            [
                new CellCompletedRecord
                {
                    CellData = new TestCellData
                    {
                        Barcode = "BAR-VALID",
                        CellResult = true,
                        CompletedTime = DateTime.UtcNow
                    }
                },
                new CellCompletedRecord
                {
                    CellData = new TestCellData
                    {
                        Barcode = "BAR-INVALID",
                        CellResult = null,
                        CompletedTime = DateTime.UtcNow
                    }
                }
            ],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("pass_station_cell_result_required", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenHttpStartedThenCanceled_ShouldPropagateTokenWithoutLaterUploadSideEffect()
    {
        var postStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudHttp = new FakeCloudHttpClient
        {
            PostStarted = postStarted,
            PostWait = neverRelease.Task
        };
        var uploader = new StandardPassStationCloudUploader(
            new FakeCloudApiEndpointProvider(),
            cloudHttp);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var uploadTask = uploader.UploadAsync(
            new ProcessUploadContext(CreateOnlineDeviceService().CurrentDevice!),
            "OtherProcess",
            [
                new CellCompletedRecord
                {
                    CellData = new TestCellData
                    {
                        Barcode = "BAR-CANCEL",
                        CellResult = true,
                        CompletedTime = DateTime.UtcNow
                    }
                }
            ],
            cts.Token);
        await postStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploadTask);
        Assert.Equal(1, cloudHttp.PostCallCount);
        Assert.Equal(0, cloudHttp.CompletedPostCount);
        Assert.Equal([cts.Token], cloudHttp.PostCancellationTokens);
        Assert.Single(cloudHttp.PostPayloads);
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
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.EquipmentStatus",
            CellData = new TestCellData
            {
                Barcode = "STATUS-CLOUD-01",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("cloud_plc_device_status_endpoint_missing", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, diagnostics.Snapshot.LastOutcome);
        Assert.Equal("cloud_plc_device_status_endpoint_missing", diagnostics.Snapshot.LastReasonCode);
        Assert.Equal("PLC-HM-01", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("TestPlugin", diagnostics.Snapshot.LastModuleId);
        Assert.Equal("TestPlugin.EquipmentStatus", diagnostics.Snapshot.LastTaskKey);
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
            PlcCode = "PLC-A",
            NetworkDeviceId = 1001,
            DeviceName = "PLC-A",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestCellData
            {
                DeviceCode = "PLC-A",
                Barcode = "BAR-PLC-A",
                CellResult = true,
                CompletedTime = DateTime.UtcNow
            }
        };
        var plcBRecord = new CellCompletedRecord
        {
            PlcCode = "PLC-B",
            NetworkDeviceId = 1002,
            DeviceName = "PLC-B",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Realtime",
            CellData = new TestCellData
            {
                DeviceCode = "PLC-B",
                Barcode = "BAR-PLC-B",
                CellResult = true,
                CompletedTime = DateTime.UtcNow
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
    public async Task ProcessBatchAsync_WhenProductionAndDeviceStatusSharePlc_ShouldBlockWholeDirectCallWithoutPartialSuccess()
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
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.Realtime",
            CellData = new TestCellData
            {
                Barcode = "PROD-CLOUD-01",
                CellResult = true,
                CompletedTime = DateTime.UtcNow,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };
        var statusRecord = new CellCompletedRecord
        {
            NetworkDeviceId = 2001,
            DeviceName = "PLC-HM-01",
            ModuleId = "TestPlugin",
            TaskKey = "TestPlugin.EquipmentStatus",
            CellData = new TestCellData
            {
                Barcode = "STATUS-CLOUD-02",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

        var result = await consumer.ProcessBatchAsync(
            [productionRecord, statusRecord],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("cloud_plc_device_status_endpoint_missing", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Equal("OtherProcess", diagnostics.Snapshot.LastProcessType);
        Assert.Equal("PLC-HM-01", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("TestPlugin.EquipmentStatus", diagnostics.Snapshot.LastTaskKey);
        Assert.Equal("设备状态上传", diagnostics.Snapshot.LastScenario);
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenDeviceStatusIsInLaterProcessAndPlc_ShouldPreflightWholeBatchBeforeAnyHttp()
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
            DeviceName = "PLC-PRODUCTION",
            ModuleId = "TestPluginAlpha",
            TaskKey = "TestPluginAlpha.Realtime",
            CellData = new TestCellData
            {
                Barcode = "PROD-CLOUD-FIRST",
                CellResult = true,
                CompletedTime = DateTime.UtcNow,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };
        var statusRecord = new CellCompletedRecord
        {
            NetworkDeviceId = 3002,
            DeviceName = "PLC-STATUS-LATER",
            ModuleId = "TestPluginBeta",
            TaskKey = "TestPluginBeta.EquipmentStatus",
            CellData = new TestProcessCellData
            {
                Barcode = "STATUS-CLOUD-LATER",
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

        var result = await consumer.ProcessBatchAsync(
            [productionRecord, statusRecord],
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, result.Outcome);
        Assert.Equal("cloud_plc_device_status_endpoint_missing", result.ReasonCode);
        Assert.Equal(0, cloudHttp.PostCallCount);
        Assert.Empty(cloudHttp.PostPayloads);
        Assert.Equal(TestProcessCellData.ProcessTypeKey, diagnostics.Snapshot.LastProcessType);
        Assert.Equal("PLC-STATUS-LATER", diagnostics.Snapshot.LastDeviceName);
        Assert.Equal("TestPluginBeta.EquipmentStatus", diagnostics.Snapshot.LastTaskKey);
        Assert.Equal("设备状态上传", diagnostics.Snapshot.LastScenario);
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
                Barcode = "BAR-CLOUD-BAD-PATH",
                CellResult = true,
                CompletedTime = DateTime.UtcNow
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

        public string GetPassStationBatchPath(string typeKey)
            => throw new InvalidOperationException("Pass station path is invalid.");

        public string GetCapacityHourlyPath() => "/api/v1/edge/capacity/hourly";
        public string GetCapacitySummaryPath() => "/api/v1/edge/capacity/summary";
        public string GetCapacitySummaryRangePath() => "/api/v1/edge/capacity/summary/range";
    }

}
