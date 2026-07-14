using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;
using IIoT.Edge.Application.Common.Persistence;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EdgeSyncDiagnosticsQueryBehaviorTests
{
    [Fact]
    public async Task GetCurrentAsync_ShouldAggregateCloudAndMesDiagnostics()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-01",
            ClientCode = "LINE-01",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });
        deviceService.MarkUploadGateBlocked(EdgeUploadBlockReason.UploadTokenRejected, DateTimeOffset.UtcNow);

        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        cloudDiagnostics.RecordResult(
            "Capacity",
            CloudCallResult.Failure(CloudCallOutcome.UnauthorizedAfterRetry, "upload_token_rejected"),
            new CloudUploadDiagnosticsContext(
                DeviceName: "PLC-CLOUD-DIAG",
                ModuleId: "CapacityModule",
                TaskKey: "Capacity.Sync",
                Scenario: "产能上传"));
        cloudDiagnostics.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);

        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();
        mesRetryDiagnostics.SetRuntimeState(MesRetryRuntimeState.Backoff);

        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        mesDiagnostics.RecordSuccess("Homogenization");
        mesDiagnostics.RecordFailure("TestProcess", "mes timeout");

        var cloudRetryStore = new FakeFailedRecordStore();
        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "TestProcess",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var mesRetryStore = new FakeFailedRecordStore();
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "TestProcess",
            FailedTarget = "MES",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var deviceLogBufferStore = new FakeDeviceLogBufferStore();
        deviceLogBufferStore.Records.Add(new DeviceLogRecord { Id = 10, CreatedAt = DateTime.UtcNow.ToString("O") });

        var capacityBufferStore = new FakeCapacityBufferStore();
        capacityBufferStore.Records.Add(new CapacityRecord
        {
            Id = 20,
            Barcode = "BC-20",
            CellResult = true,
            ShiftCode = "D",
            CompletedTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            PlcName = "PLC-A"
        });
        capacityBufferStore.HourlySummaries.Add(new BufferHourlySummaryDto
        {
            Date = "2026-04-18",
            Hour = 10,
            MinuteBucket = 0,
            ShiftCode = "D",
            PlcName = "PLC-A"
        });
        var contextStore = new FakeProductionContextStore
        {
            PersistenceDiagnostics = new ProductionContextPersistenceDiagnostics(
                CorruptFileCount: 2,
                LastCorruptDetectedAt: new DateTime(2026, 4, 18, 12, 30, 0))
        };

        var query = new EdgeSyncDiagnosticsQuery(
            contextStore,
            deviceService,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics,
            cloudRetryStore,
            mesRetryStore,
            deviceLogBufferStore,
            capacityBufferStore);

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Edge-01", snapshot.DeviceName);
        Assert.Equal(EdgeUploadBlockReason.UploadTokenRejected, snapshot.Cloud.BlockReason);
        Assert.True(snapshot.Cloud.IsPausedWaitingForRecovery);
        Assert.Equal(1, snapshot.Cloud.PendingRetryCount);
        Assert.Equal(1, snapshot.Cloud.PendingPassStationCount);
        Assert.Equal(1, snapshot.Cloud.PendingDeviceLogCount);
        Assert.Equal(1, snapshot.Cloud.PendingCapacityCount);
        Assert.Equal("PLC-CLOUD-DIAG", snapshot.Cloud.LastDeviceName);
        Assert.Equal("CapacityModule", snapshot.Cloud.LastModuleId);
        Assert.Equal("Capacity.Sync", snapshot.Cloud.LastTaskKey);
        Assert.Equal("产能上传", snapshot.Cloud.LastScenario);
        Assert.Equal(MesRetryRuntimeState.Backoff, snapshot.Mes.RuntimeState);
        Assert.Equal(1, snapshot.Mes.PendingRetryCount);
        Assert.Equal("mes timeout", snapshot.Mes.LastFailureReason);
        Assert.Equal(2, snapshot.Mes.Channels.Count);
        Assert.Equal(2, snapshot.ContextPersistence.CorruptFileCount);
        Assert.False(snapshot.Cloud.IsPersistenceFaulted);
        Assert.False(snapshot.Mes.IsPersistenceFaulted);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenMesChannelIsBlocked_ShouldNotTreatBlockedAsFailure()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-Blocked",
            ClientCode = "LINE-BLOCKED",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        mesDiagnostics.RecordFailure("FailedProcess", "mes timeout");
        var failedChannel = mesDiagnostics.Get("FailedProcess");
        mesDiagnostics.RecordBlocked("BlockedProcess", "MES 已启用，请先选择主批计划。");

        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            deviceService,
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            mesDiagnostics,
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeDeviceLogBufferStore(),
            new FakeCapacityBufferStore());

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("mes timeout", snapshot.Mes.LastFailureReason);
        Assert.Equal(failedChannel!.LastAttemptAt, snapshot.Mes.LastFailureAt);
        var blockedChannel = Assert.Single(snapshot.Mes.Channels, x => x.ProcessType == "BlockedProcess");
        Assert.Equal("Blocked", blockedChannel.LastResult);
        Assert.Null(blockedChannel.LastFailureReason);
        Assert.Equal("MES 已启用，请先选择主批计划。", blockedChannel.LastBlockedReason);
        Assert.NotNull(blockedChannel.LastBlockedAt);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenCloudIsBlocked_ShouldNotTreatBlockedAsFailure()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-Cloud-Blocked",
            ClientCode = "LINE-CLOUD-BLOCKED",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        cloudDiagnostics.RecordBlocked(
            "OtherProcess",
            "missing_upload_token",
            "缺少上传令牌。",
            new CloudUploadDiagnosticsContext(
                DeviceName: "PLC-CLOUD-BLOCKED",
                ModuleId: "TestPluginAlpha",
                TaskKey: "TestPlugin.RealtimeSampleUpload",
                Scenario: "生产上传"));

        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            deviceService,
            cloudDiagnostics,
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeDeviceLogBufferStore(),
            new FakeCapacityBufferStore());

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CloudCallOutcome.SkippedUploadNotReady, snapshot.Cloud.LastOutcome);
        Assert.Equal("missing_upload_token", snapshot.Cloud.LastReasonCode);
        Assert.Null(snapshot.Cloud.LastFailureAt);
        Assert.NotNull(snapshot.Cloud.LastBlockedAt);
        Assert.Equal("缺少上传令牌。", snapshot.Cloud.LastBlockedReason);
        Assert.Equal("PLC-CLOUD-BLOCKED", snapshot.Cloud.LastDeviceName);
        Assert.Equal("TestPluginAlpha", snapshot.Cloud.LastModuleId);
        Assert.Equal("TestPlugin.RealtimeSampleUpload", snapshot.Cloud.LastTaskKey);
        Assert.Equal("生产上传", snapshot.Cloud.LastScenario);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenCloudOrMesPersistenceFails_ShouldReturnVisibleFaultSnapshot()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-02",
            ClientCode = "LINE-02",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();
        var cloudRetryStore = new FakeFailedRecordStore
        {
            CloudCountException = new PersistenceAccessException("cloud retry count failed", new InvalidOperationException("cloud-count"))
        };
        var mesRetryStore = new FakeFailedRecordStore
        {
            MesCountException = new PersistenceAccessException("mes retry count failed", new InvalidOperationException("mes-count"))
        };
        var deviceLogBufferStore = new FakeDeviceLogBufferStore
        {
            CountException = new PersistenceAccessException("device log count failed", new InvalidOperationException("log-count"))
        };
        var capacityBufferStore = new FakeCapacityBufferStore
        {
            CountException = new PersistenceAccessException("capacity count failed", new InvalidOperationException("capacity-count"))
        };

        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            deviceService,
            cloudDiagnostics,
            mesRetryDiagnostics,
            new FakeMesUploadDiagnosticsStore(),
            cloudRetryStore,
            mesRetryStore,
            deviceLogBufferStore,
            capacityBufferStore);

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.Cloud.IsPersistenceFaulted);
        Assert.Contains("cloud retry count failed", snapshot.Cloud.PersistenceFaultMessage, StringComparison.Ordinal);
        Assert.NotNull(snapshot.Cloud.LastPersistenceFaultAt);
        Assert.Equal(0, snapshot.Cloud.PendingRetryCount);
        Assert.Equal(0, snapshot.Cloud.PendingPassStationCount);
        Assert.Equal(0, snapshot.Cloud.PendingDeviceLogCount);
        Assert.Equal(0, snapshot.Cloud.PendingCapacityCount);

        Assert.True(snapshot.Mes.IsPersistenceFaulted);
        Assert.Contains("mes retry count failed", snapshot.Mes.PersistenceFaultMessage, StringComparison.Ordinal);
        Assert.NotNull(snapshot.Mes.LastPersistenceFaultAt);
        Assert.Equal(0, snapshot.Mes.PendingRetryCount);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenCountsAreDelayed_ShouldRemainAsynchronous()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-03",
            ClientCode = "LINE-03",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var countRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudRetryCountStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mesRetryCountStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deviceLogCountStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capacityCountStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var cloudRetryStore = new FakeFailedRecordStore
        {
            CloudCountStarted = cloudRetryCountStarted,
            CloudCountWait = countRelease.Task
        };
        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "TestProcess",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var mesRetryStore = new FakeFailedRecordStore
        {
            MesCountStarted = mesRetryCountStarted,
            MesCountWait = countRelease.Task
        };
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "TestProcess",
            FailedTarget = "MES",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var deviceLogBufferStore = new FakeDeviceLogBufferStore
        {
            CountStarted = deviceLogCountStarted,
            CountWait = countRelease.Task
        };
        deviceLogBufferStore.Records.Add(new DeviceLogRecord { Id = 10, CreatedAt = DateTime.UtcNow.ToString("O") });

        var capacityBufferStore = new FakeCapacityBufferStore
        {
            CountStarted = capacityCountStarted,
            CountWait = countRelease.Task
        };
        capacityBufferStore.Records.Add(new CapacityRecord
        {
            Id = 20,
            Barcode = "BC-20",
            CellResult = true,
            ShiftCode = "D",
            CompletedTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            PlcName = "PLC-A"
        });

        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            deviceService,
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            cloudRetryStore,
            mesRetryStore,
            deviceLogBufferStore,
            capacityBufferStore);

        var snapshotTask = query.GetCurrentAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(
            cloudRetryCountStarted.Task,
            mesRetryCountStarted.Task,
            deviceLogCountStarted.Task,
            capacityCountStarted.Task);

        Assert.False(snapshotTask.IsCompleted);

        countRelease.SetResult();
        var snapshot = await snapshotTask;
        Assert.Equal(1, snapshot.Cloud.PendingRetryCount);
        Assert.Equal(1, snapshot.Cloud.PendingPassStationCount);
        Assert.Equal(1, snapshot.Cloud.PendingDeviceLogCount);
        Assert.Equal(1, snapshot.Cloud.PendingCapacityCount);
        Assert.Equal(1, snapshot.Mes.PendingRetryCount);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldExposeDeadLetterDiagnostics()
    {
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-04",
            ClientCode = "LINE-04",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        await cloudDeadLetters.SaveAsync(new DeadLetterRecord
        {
            Id = 1,
            ProcessType = "TestProcess",
            FailedTarget = "Cloud",
            SourceTable = "failed_cloud_records",
            FailureStage = "RetryDeserialize",
            FailureReason = "bad json",
            CellDataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });

        var mesDeadLetters = new FakeMesDeadLetterStore();
        await mesDeadLetters.SaveAsync(new DeadLetterRecord
        {
            Id = 2,
            ProcessType = "Homogenization",
            FailedTarget = "MES",
            SourceTable = "failed_mes_records",
            FailureStage = "RetryDeserialize",
            FailureReason = "bad json",
            CellDataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });

        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            deviceService,
            new FakeCloudDiagnosticsStore(),
            new FakeMesRetryDiagnosticsStore(),
            new FakeMesUploadDiagnosticsStore(),
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeDeviceLogBufferStore(),
            new FakeCapacityBufferStore(),
            cloudDeadLetterStore: cloudDeadLetters,
            mesDeadLetterStore: mesDeadLetters);

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, snapshot.Cloud.DeadLetters?.TotalCount);
        Assert.Equal("TestProcess", snapshot.Cloud.DeadLetters?.GroupSummary.Single().ProcessType);
        Assert.Equal(1, snapshot.Mes.DeadLetters?.TotalCount);
        Assert.Equal("Homogenization", snapshot.Mes.DeadLetters?.LatestRecords.Single().ProcessType);
    }

    [Fact]
    public async Task GetCurrentAsync_ShouldResolveRegisteredProcessDisplayNames()
    {
        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        cloudDiagnostics.RecordResult(
            "CustomProcess",
            CloudCallResult.Failure(CloudCallOutcome.Exception, "custom_failure"));

        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        mesDiagnostics.RecordFailure("CustomProcess", "mes timeout");
        mesDiagnostics.RecordFailure(
            "CustomProcess.Realtime",
            "realtime timeout",
            new MesUploadDiagnosticsContext("PLC-A", "CustomProcess", "CustomProcess.Realtime", "生产上传"));
        mesDiagnostics.RecordFailure("UnknownProcess", "unknown timeout");

        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        await cloudDeadLetters.SaveAsync(new DeadLetterRecord
        {
            Id = 1,
            ProcessType = "CustomProcess",
            FailedTarget = "Cloud",
            SourceTable = "failed_cloud_records",
            FailureStage = "RetryDeserialize",
            FailureReason = "bad json",
            CellDataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await cloudDeadLetters.SaveAsync(new DeadLetterRecord
        {
            Id = 2,
            ProcessType = "UnknownProcess",
            FailedTarget = "Cloud",
            SourceTable = "failed_cloud_records",
            FailureStage = "RetryDeserialize",
            FailureReason = "bad json",
            CellDataJson = "{}",
            CreatedAt = DateTime.UtcNow
        });

        var module = new StubProcessModule("CustomProcess", "Custom Display");
        var query = new EdgeSyncDiagnosticsQuery(
            new FakeProductionContextStore(),
            new FakeDeviceService(),
            cloudDiagnostics,
            new FakeMesRetryDiagnosticsStore(),
            mesDiagnostics,
            new FakeFailedRecordStore(),
            new FakeFailedRecordStore(),
            new FakeDeviceLogBufferStore(),
            new FakeCapacityBufferStore(),
            cloudDeadLetterStore: cloudDeadLetters,
            modules: [module]);

        var snapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Custom Display", snapshot.Cloud.LastProcessDisplayName);
        Assert.Equal(
            "Custom Display",
            snapshot.Mes.Channels.Single(x => x.ProcessType == "CustomProcess").ProcessDisplayName);
        var realtimeChannel = snapshot.Mes.Channels.Single(x => x.ProcessType == "CustomProcess.Realtime");
        Assert.Equal("Custom Display", realtimeChannel.ProcessDisplayName);
        Assert.Equal("PLC-A", realtimeChannel.DeviceName);
        Assert.Equal("生产上传", realtimeChannel.Scenario);
        Assert.Null(snapshot.Mes.Channels.Single(x => x.ProcessType == "UnknownProcess").ProcessDisplayName);
        Assert.Equal(
            "Custom Display",
            snapshot.Cloud.DeadLetters?.GroupSummary.Single(x => x.ProcessType == "CustomProcess").ProcessDisplayName);
        Assert.Null(snapshot.Cloud.DeadLetters?.GroupSummary.Single(x => x.ProcessType == "UnknownProcess").ProcessDisplayName);

        module.DisplayName = "Custom Display EN";
        var refreshedSnapshot = await query.GetCurrentAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Custom Display EN", refreshedSnapshot.Cloud.LastProcessDisplayName);
        Assert.Equal(
            "Custom Display EN",
            refreshedSnapshot.Mes.Channels.Single(x => x.ProcessType == "CustomProcess").ProcessDisplayName);
        Assert.Equal(
            "Custom Display EN",
            refreshedSnapshot.Mes.Channels.Single(x => x.ProcessType == "CustomProcess.Realtime").ProcessDisplayName);
        Assert.Equal(
            "Custom Display EN",
            refreshedSnapshot.Cloud.DeadLetters?.GroupSummary.Single(x => x.ProcessType == "CustomProcess").ProcessDisplayName);
    }

    private sealed class StubProcessModule(string processType, string displayName) : IEdgeProcessModule
    {
        public string ModuleId => processType;

        public string ProcessType => processType;

        public string DisplayName { get; set; } = displayName;

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
        }
    }
}
