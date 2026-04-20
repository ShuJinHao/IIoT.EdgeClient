using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class MonitorQueriesBehaviorTests
{
    [Fact]
    public async Task Handle_WhenUploadGateIsReady_ShouldExposeStructuredReadyStatus()
    {
        var deviceService = new FakeDeviceService();
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();

        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "Edge-A",
            ClientCode = "LINE-01",
            ProcessId = Guid.NewGuid(),
            UploadAccessToken = "device-token",
            UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        cloudDiagnostics.RecordResult("Injection", CloudCallResult.Success());
        mesDiagnostics.RecordSuccess("Injection");

        var handler = CreateHandler(
            deviceService,
            cloudRetryStore,
            mesRetryStore,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(EdgeUploadGateState.Ready, snapshot.CloudSync.GateState);
        Assert.Equal(CloudCallOutcome.Success, snapshot.CloudSync.LastOutcome);
        Assert.Equal(MesRetryRuntimeState.Idle, snapshot.MesSync.RuntimeState);
    }

    [Fact]
    public async Task Handle_WhenUploadGateIsBlocked_ShouldExposeQueueCountsAndFailureState()
    {
        var cloudRetryStore = new FakeFailedRecordStore();
        var mesRetryStore = new FakeFailedRecordStore();
        var cloudDiagnostics = new FakeCloudDiagnosticsStore();
        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();
        var deviceService = new FakeDeviceService
        {
            CurrentDevice = new DeviceSession
            {
                DeviceId = Guid.NewGuid(),
                DeviceName = "Edge-B",
                ClientCode = "LINE-02",
                ProcessId = Guid.NewGuid(),
                UploadAccessToken = "expired-token",
                UploadAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        };
        deviceService.SetUploadGate(new EdgeUploadGateSnapshot
        {
            State = EdgeUploadGateState.Blocked,
            Reason = EdgeUploadBlockReason.ExpiredUploadToken,
            TokenExpiresAtUtc = deviceService.CurrentDevice.UploadAccessTokenExpiresAtUtc,
            LastBootstrapFailedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        cloudRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 1,
            Channel = "Cloud",
            ProcessType = "Injection",
            FailedTarget = "Cloud",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "Injection",
            FailedTarget = "MES",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });
        cloudDiagnostics.RecordResult("Injection", CloudCallResult.Failure(CloudCallOutcome.SkippedUploadNotReady, "expired_upload_token"));
        cloudDiagnostics.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);
        mesRetryDiagnostics.SetRuntimeState(MesRetryRuntimeState.Backoff);
        mesDiagnostics.RecordFailure("Injection", "mes endpoint timeout");

        var handler = CreateHandler(
            deviceService,
            cloudRetryStore,
            mesRetryStore,
            cloudDiagnostics,
            mesRetryDiagnostics,
            mesDiagnostics);

        var snapshots = await handler.Handle(new GetMonitorSnapshotQuery(), CancellationToken.None);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(EdgeUploadGateState.Blocked, snapshot.CloudSync.GateState);
        Assert.Equal(EdgeUploadBlockReason.ExpiredUploadToken, snapshot.CloudSync.BlockReason);
        Assert.True(snapshot.CloudSync.IsPausedWaitingForRecovery);
        Assert.Equal(1, snapshot.CloudSync.PendingRetryCount);
        Assert.Equal(MesRetryRuntimeState.Backoff, snapshot.MesSync.RuntimeState);
        Assert.Equal(1, snapshot.MesSync.PendingRetryCount);
        Assert.Equal("mes endpoint timeout", snapshot.MesSync.LastFailureReason);
    }

    private static GetMonitorSnapshotHandler CreateHandler(
        FakeDeviceService deviceService,
        FakeFailedRecordStore cloudRetryStore,
        FakeFailedRecordStore mesRetryStore,
        FakeCloudDiagnosticsStore cloudDiagnostics,
        FakeMesRetryDiagnosticsStore mesRetryDiagnostics,
        FakeMesUploadDiagnosticsStore mesDiagnostics)
    {
        var contextStore = new FakeProductionContextStore();

        return new GetMonitorSnapshotHandler(
            contextStore,
            new EdgeSyncDiagnosticsQuery(
                contextStore,
                deviceService,
                cloudDiagnostics,
                mesRetryDiagnostics,
                mesDiagnostics,
                cloudRetryStore,
                mesRetryStore,
                new FakeDeviceLogBufferStore(),
                new FakeCapacityBufferStore()));
    }
}
