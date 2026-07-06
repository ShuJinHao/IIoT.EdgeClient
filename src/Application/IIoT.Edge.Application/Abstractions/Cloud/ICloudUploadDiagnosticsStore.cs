using IIoT.Edge.Application.Abstractions.Shared;

namespace IIoT.Edge.Application.Abstractions.Cloud;

public enum CloudRetryRuntimeState
{
    Idle = 0,
    Retrying = 1,
    Backoff = 2,
    WaitingForRecovery = 3
}

public sealed record CloudUploadDiagnosticsSnapshot(
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    DateTime? LastBlockedAt,
    CloudCallOutcome LastOutcome,
    string LastReasonCode,
    string? LastBlockedReason,
    string? LastProcessType,
    CloudRetryRuntimeState RuntimeState,
    bool IsCapacityBlocked,
    CapacityBlockedChannel? BlockedChannel,
    string BlockedReason,
    DateTime? LastCapacityBlockAt,
    string? LastDeviceName = null,
    string? LastModuleId = null,
    string? LastTaskKey = null,
    string? LastScenario = null);

public sealed record CloudUploadDiagnosticsContext(
    string? DeviceName = null,
    string? ModuleId = null,
    string? TaskKey = null,
    string? Scenario = null);

public interface ICloudUploadDiagnosticsStore : IRetryDiagnosticsStore<CloudRetryRuntimeState>
{
    CloudUploadDiagnosticsSnapshot Snapshot { get; }

    void RecordResult(
        string? processType,
        CloudCallResult result,
        CloudUploadDiagnosticsContext? context = null);

    void RecordBlocked(
        string? processType,
        string reasonCode,
        string? blockedReason = null,
        CloudUploadDiagnosticsContext? context = null);

    void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null);

    void ClearCapacityBlocked();
}
