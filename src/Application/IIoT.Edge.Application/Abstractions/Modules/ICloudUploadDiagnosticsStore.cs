using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.Application.Abstractions.Modules;

public enum CloudRetryRuntimeState
{
    Idle = 0,
    Retrying = 1,
    Backoff = 2,
    WaitingForRecovery = 3
}

public enum CapacityBlockedChannel
{
    Retry = 0,
    Fallback = 1
}

public sealed record CloudUploadDiagnosticsSnapshot(
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    CloudCallOutcome LastOutcome,
    string LastReasonCode,
    string? LastProcessType,
    CloudRetryRuntimeState RuntimeState,
    bool IsCapacityBlocked,
    CapacityBlockedChannel? BlockedChannel,
    string BlockedReason,
    DateTime? LastCapacityBlockAt);

public interface ICloudUploadDiagnosticsStore
{
    CloudUploadDiagnosticsSnapshot Snapshot { get; }

    void RecordResult(string? processType, CloudCallResult result);

    void SetRuntimeState(CloudRetryRuntimeState state);

    void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null);

    void ClearCapacityBlocked();
}
