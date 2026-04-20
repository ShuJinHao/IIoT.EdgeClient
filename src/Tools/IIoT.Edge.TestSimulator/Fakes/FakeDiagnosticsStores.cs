using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.TestSimulator.Fakes;

public sealed class FakeCloudUploadDiagnosticsStore : ICloudUploadDiagnosticsStore
{
    public CloudUploadDiagnosticsSnapshot Snapshot { get; private set; } = new(
        LastAttemptAt: null,
        LastSuccessAt: null,
        LastFailureAt: null,
        LastOutcome: CloudCallOutcome.Success,
        LastReasonCode: "none",
        LastProcessType: null,
        RuntimeState: CloudRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void RecordResult(string? processType, CloudCallResult result)
    {
        var now = DateTime.Now;
        Snapshot = Snapshot with
        {
            LastAttemptAt = now,
            LastSuccessAt = result.IsSuccess ? now : Snapshot.LastSuccessAt,
            LastFailureAt = result.IsSuccess ? Snapshot.LastFailureAt : now,
            LastOutcome = result.Outcome,
            LastReasonCode = result.ReasonCode,
            LastProcessType = processType
        };
    }

    public void SetRuntimeState(CloudRetryRuntimeState state)
    {
        Snapshot = Snapshot with
        {
            RuntimeState = state
        };
    }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = true,
            BlockedChannel = channel,
            BlockedReason = string.IsNullOrWhiteSpace(blockedReason) ? "unknown" : blockedReason,
            LastCapacityBlockAt = occurredAt ?? DateTime.Now
        };
    }

    public void ClearCapacityBlocked()
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = false,
            BlockedChannel = null,
            BlockedReason = "none"
        };
    }
}

public sealed class FakeMesRetryDiagnosticsStore : IMesRetryDiagnosticsStore
{
    public MesRetryDiagnosticsSnapshot Snapshot { get; private set; } = new(
        MesRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public void SetRuntimeState(MesRetryRuntimeState state)
    {
        Snapshot = Snapshot with
        {
            RuntimeState = state
        };
    }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = true,
            BlockedChannel = channel,
            BlockedReason = string.IsNullOrWhiteSpace(blockedReason) ? "unknown" : blockedReason,
            LastCapacityBlockAt = occurredAt ?? DateTime.Now
        };
    }

    public void ClearCapacityBlocked()
    {
        Snapshot = Snapshot with
        {
            IsCapacityBlocked = false,
            BlockedChannel = null,
            BlockedReason = "none"
        };
    }
}
