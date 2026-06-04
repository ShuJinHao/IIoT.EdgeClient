using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;

using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Shared;
namespace IIoT.Edge.Shell.Core;

public sealed class CloudUploadDiagnosticsStore
    : CapacityBlockableDiagnosticsStore<CloudUploadDiagnosticsSnapshot, CloudRetryRuntimeState>,
        ICloudUploadDiagnosticsStore
{
    public CloudUploadDiagnosticsStore()
        : base(new CloudUploadDiagnosticsSnapshot(
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
            LastCapacityBlockAt: null))
    {
    }

    public CloudUploadDiagnosticsSnapshot Snapshot => GetSnapshot();

    public void RecordResult(string? processType, CloudCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = DateTime.UtcNow;
        UpdateSnapshot(snapshot => snapshot with
            {
                LastAttemptAt = now,
                LastSuccessAt = result.IsSuccess ? now : snapshot.LastSuccessAt,
                LastFailureAt = result.IsSuccess ? snapshot.LastFailureAt : now,
                LastOutcome = result.Outcome,
                LastReasonCode = string.IsNullOrWhiteSpace(result.ReasonCode) ? "unknown" : result.ReasonCode,
                LastProcessType = processType
            });
    }

    public void SetRuntimeState(CloudRetryRuntimeState state)
        => SetRuntimeStateCore(
            state,
            static snapshot => snapshot.RuntimeState,
            static (snapshot, runtimeState) => snapshot with { RuntimeState = runtimeState });

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
        => MarkCapacityBlockedCore(
            channel,
            blockedReason,
            occurredAt,
            static (snapshot, blockedChannel, reason, blockTime) => snapshot with
            {
                IsCapacityBlocked = true,
                BlockedChannel = blockedChannel,
                BlockedReason = reason,
                LastCapacityBlockAt = blockTime
            });

    public void ClearCapacityBlocked()
        => ClearCapacityBlockedCore(
            static snapshot => snapshot.IsCapacityBlocked,
            static snapshot => snapshot with
            {
                IsCapacityBlocked = false,
                BlockedChannel = null,
                BlockedReason = "none"
            });
}
