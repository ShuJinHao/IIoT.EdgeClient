using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.DataPipeline.DeviceLog;
using IIoT.Edge.Application.Common.Persistence;

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
        cloudDiagnostics.RecordResult("Capacity", CloudCallResult.Failure(CloudCallOutcome.UnauthorizedAfterRetry, "upload_token_rejected"));
        cloudDiagnostics.SetRuntimeState(CloudRetryRuntimeState.WaitingForRecovery);

        var mesRetryDiagnostics = new FakeMesRetryDiagnosticsStore();
        mesRetryDiagnostics.SetRuntimeState(MesRetryRuntimeState.Backoff);

        var mesDiagnostics = new FakeMesUploadDiagnosticsStore();
        mesDiagnostics.RecordSuccess("Injection");
        mesDiagnostics.RecordFailure("Stacking", "mes timeout");

        var cloudRetryStore = new FakeFailedRecordStore();
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

        var mesRetryStore = new FakeFailedRecordStore();
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "Stacking",
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

        var snapshot = await query.GetCurrentAsync();

        Assert.Equal("Edge-01", snapshot.DeviceName);
        Assert.Equal(EdgeUploadBlockReason.UploadTokenRejected, snapshot.Cloud.BlockReason);
        Assert.True(snapshot.Cloud.IsPausedWaitingForRecovery);
        Assert.Equal(1, snapshot.Cloud.PendingRetryCount);
        Assert.Equal(1, snapshot.Cloud.PendingDeviceLogCount);
        Assert.Equal(1, snapshot.Cloud.PendingCapacityCount);
        Assert.Equal(MesRetryRuntimeState.Backoff, snapshot.Mes.RuntimeState);
        Assert.Equal(1, snapshot.Mes.PendingRetryCount);
        Assert.Equal("mes timeout", snapshot.Mes.LastFailureReason);
        Assert.Equal(2, snapshot.Mes.Channels.Count);
        Assert.Equal(2, snapshot.ContextPersistence.CorruptFileCount);
        Assert.False(snapshot.Cloud.IsPersistenceFaulted);
        Assert.False(snapshot.Mes.IsPersistenceFaulted);
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

        var snapshot = await query.GetCurrentAsync();

        Assert.True(snapshot.Cloud.IsPersistenceFaulted);
        Assert.Contains("cloud retry count failed", snapshot.Cloud.PersistenceFaultMessage, StringComparison.Ordinal);
        Assert.NotNull(snapshot.Cloud.LastPersistenceFaultAt);
        Assert.Equal(0, snapshot.Cloud.PendingRetryCount);
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

        var cloudRetryStore = new FakeFailedRecordStore
        {
            CloudCountDelay = TimeSpan.FromMilliseconds(120),
            MesCountDelay = TimeSpan.FromMilliseconds(120)
        };
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

        var mesRetryStore = new FakeFailedRecordStore
        {
            MesCountDelay = TimeSpan.FromMilliseconds(120)
        };
        mesRetryStore.PendingRecords.Add(new FailedCellRecord
        {
            Id = 2,
            Channel = "MES",
            ProcessType = "Stacking",
            FailedTarget = "MES",
            CellDataJson = "{}",
            ErrorMessage = "seed",
            NextRetryTime = DateTime.UtcNow
        });

        var deviceLogBufferStore = new FakeDeviceLogBufferStore
        {
            CountDelay = TimeSpan.FromMilliseconds(120)
        };
        deviceLogBufferStore.Records.Add(new DeviceLogRecord { Id = 10, CreatedAt = DateTime.UtcNow.ToString("O") });

        var capacityBufferStore = new FakeCapacityBufferStore
        {
            CountDelay = TimeSpan.FromMilliseconds(120)
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

        var snapshotTask = query.GetCurrentAsync();

        Assert.False(snapshotTask.IsCompleted);

        var snapshot = await snapshotTask;
        Assert.Equal(1, snapshot.Cloud.PendingRetryCount);
        Assert.Equal(1, snapshot.Cloud.PendingDeviceLogCount);
        Assert.Equal(1, snapshot.Cloud.PendingCapacityCount);
        Assert.Equal(1, snapshot.Mes.PendingRetryCount);
    }
}
