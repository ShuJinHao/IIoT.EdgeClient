using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record DeadLetterDiagnosticsSnapshot(
    int TotalCount,
    IReadOnlyList<DeadLetterGroupSummary> GroupSummary,
    IReadOnlyList<DeadLetterRecord> LatestRecords,
    bool IsPersistenceFaulted,
    DateTime? LastPersistenceFaultAt,
    string? PersistenceFaultMessage)
{
    public static DeadLetterDiagnosticsSnapshot Empty { get; } = new(0, [], [], false, null, null);
}

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
    string? PersistenceFaultMessage,
    ExternalHeartbeatSnapshot? Heartbeat = null,
    DeadLetterDiagnosticsSnapshot? DeadLetters = null,
    string? LastProcessDisplayName = null);

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
    string? PersistenceFaultMessage,
    ExternalHeartbeatSnapshot? Heartbeat = null,
    DeadLetterDiagnosticsSnapshot? DeadLetters = null);

public sealed record EdgeSyncDiagnosticsSnapshot(
    string DeviceName,
    CloudSyncDiagnosticsSnapshot Cloud,
    MesSyncDiagnosticsSnapshot Mes,
    ProductionContextPersistenceDiagnostics ContextPersistence);

public interface IEdgeSyncDiagnosticsQuery
{
    Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default);
}
