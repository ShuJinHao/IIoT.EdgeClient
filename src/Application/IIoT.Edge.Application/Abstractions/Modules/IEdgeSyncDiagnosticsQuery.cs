using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record CloudSyncDiagnosticsSnapshot(
    EdgeUploadGateState GateState,
    EdgeUploadBlockReason BlockReason,
    CloudRetryRuntimeState RuntimeState,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    CloudCallOutcome LastOutcome,
    string LastReasonCode,
    string? LastProcessType,
    int PendingRetryCount,
    int PendingDeviceLogCount,
    int PendingCapacityCount,
    bool IsPausedWaitingForRecovery,
    bool IsCapacityBlocked,
    CapacityBlockedChannel? BlockedChannel,
    string BlockedReason,
    DateTime? LastCapacityBlockAt,
    bool IsPersistenceFaulted,
    DateTime? LastPersistenceFaultAt,
    string? PersistenceFaultMessage);

public sealed record MesSyncDiagnosticsSnapshot(
    MesRetryRuntimeState RuntimeState,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    string? LastFailureReason,
    int PendingRetryCount,
    IReadOnlyList<MesChannelDiagnostics> Channels,
    bool IsCapacityBlocked,
    CapacityBlockedChannel? BlockedChannel,
    string BlockedReason,
    DateTime? LastCapacityBlockAt,
    bool IsPersistenceFaulted,
    DateTime? LastPersistenceFaultAt,
    string? PersistenceFaultMessage);

public sealed record EdgeSyncDiagnosticsSnapshot(
    string DeviceName,
    CloudSyncDiagnosticsSnapshot Cloud,
    MesSyncDiagnosticsSnapshot Mes,
    ProductionContextPersistenceDiagnostics ContextPersistence);

public interface IEdgeSyncDiagnosticsQuery
{
    Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default);
}
