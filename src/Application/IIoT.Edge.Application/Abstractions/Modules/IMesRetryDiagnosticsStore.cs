namespace IIoT.Edge.Application.Abstractions.Modules;

public enum MesRetryRuntimeState
{
    Idle = 0,
    Retrying = 1,
    Backoff = 2,
    LastFailed = 3
}

public sealed record MesRetryDiagnosticsSnapshot(
    MesRetryRuntimeState RuntimeState,
    bool IsCapacityBlocked,
    CapacityBlockedChannel? BlockedChannel,
    string BlockedReason,
    DateTime? LastCapacityBlockAt);

public interface IMesRetryDiagnosticsStore
{
    MesRetryDiagnosticsSnapshot Snapshot { get; }

    void SetRuntimeState(MesRetryRuntimeState state);

    void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null);

    void ClearCapacityBlocked();
}
