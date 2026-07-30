using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Shared;
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
            LastBlockedAt: null,
            LastOutcome: CloudCallOutcome.Success,
            LastReasonCode: "none",
            LastBlockedReason: null,
            LastProcessType: null,
            RuntimeState: CloudRetryRuntimeState.Idle,
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: "none",
            LastCapacityBlockAt: null))
    {
    }

    public CloudUploadDiagnosticsSnapshot Snapshot => GetSnapshot();

    public void RecordResult(
        string? processType,
        CloudCallResult result,
        CloudUploadDiagnosticsContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = DateTime.UtcNow;
        var isBlocked = result.Outcome == CloudCallOutcome.SkippedUploadNotReady;
        var isFailure = !result.IsSuccess && !isBlocked;
        var normalizedReasonCode = string.IsNullOrWhiteSpace(result.ReasonCode)
            ? "unknown"
            : result.ReasonCode.Trim();
        UpdateSnapshot(snapshot => snapshot with
            {
                LastAttemptAt = now,
                LastSuccessAt = result.IsSuccess ? now : snapshot.LastSuccessAt,
                LastFailureAt = isFailure ? now : snapshot.LastFailureAt,
                LastBlockedAt = isBlocked ? now : null,
                LastOutcome = result.Outcome,
                LastReasonCode = normalizedReasonCode,
                LastBlockedReason = isBlocked ? normalizedReasonCode : null,
                LastProcessType = processType,
                LastPlcCode = NormalizeIdentity(context?.PlcCode),
                LastDeviceName = context?.DeviceName,
                LastModuleId = context?.ModuleId,
                LastTaskKey = context?.TaskKey,
                LastScenario = context?.Scenario
            });
    }

    public void RecordBlocked(
        string? processType,
        string reasonCode,
        string? blockedReason = null,
        CloudUploadDiagnosticsContext? context = null)
    {
        var normalizedReasonCode = NormalizeReasonCode(reasonCode);
        var normalizedReason = NormalizeReason(blockedReason, normalizedReasonCode);
        var now = DateTime.UtcNow;
        UpdateSnapshot(snapshot => snapshot with
        {
            LastAttemptAt = now,
            LastBlockedAt = now,
            LastOutcome = CloudCallOutcome.SkippedUploadNotReady,
            LastReasonCode = normalizedReasonCode,
            LastBlockedReason = normalizedReason,
            LastProcessType = processType,
            LastPlcCode = NormalizeIdentity(context?.PlcCode),
            LastDeviceName = context?.DeviceName,
            LastModuleId = context?.ModuleId,
            LastTaskKey = context?.TaskKey,
            LastScenario = context?.Scenario
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

    private static string NormalizeReasonCode(string? reasonCode)
        => string.IsNullOrWhiteSpace(reasonCode) ? "cloud_upload_blocked" : reasonCode.Trim();

    private static string NormalizeReason(string? reason, string fallback)
        => string.IsNullOrWhiteSpace(reason) ? fallback : reason.Trim();

    private static string? NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
