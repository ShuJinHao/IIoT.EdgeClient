using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class CloudUploadDiagnosticsStore : ICloudUploadDiagnosticsStore
{
    private readonly object _sync = new();
    private string? _blockedProcessType;

    private CloudUploadDiagnosticsSnapshot _snapshot = new(
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

    public CloudUploadDiagnosticsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void RecordResult(string? processType, CloudCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                LastAttemptAt = now,
                LastSuccessAt = result.IsSuccess ? now : _snapshot.LastSuccessAt,
                LastFailureAt = result.IsSuccess ? _snapshot.LastFailureAt : now,
                LastOutcome = result.Outcome,
                LastReasonCode = string.IsNullOrWhiteSpace(result.ReasonCode) ? "unknown" : result.ReasonCode,
                LastProcessType = processType
            };
        }
    }

    public void SetRuntimeState(CloudRetryRuntimeState state)
    {
        lock (_sync)
        {
            if (_snapshot.RuntimeState == state)
            {
                return;
            }

            _snapshot = _snapshot with
            {
                RuntimeState = state
            };
        }
    }

    public void MarkCapacityBlocked(
        CapacityBlockedChannel channel,
        string blockedReason,
        string? processType = null,
        DateTime? occurredAt = null)
    {
        lock (_sync)
        {
            _blockedProcessType = processType;
            _snapshot = _snapshot with
            {
                IsCapacityBlocked = true,
                BlockedChannel = channel,
                BlockedReason = string.IsNullOrWhiteSpace(blockedReason) ? "unknown" : blockedReason,
                LastCapacityBlockAt = occurredAt ?? DateTime.UtcNow
            };
        }
    }

    public void ClearCapacityBlocked()
    {
        lock (_sync)
        {
            if (!_snapshot.IsCapacityBlocked)
            {
                return;
            }

            _blockedProcessType = null;
            _snapshot = _snapshot with
            {
                IsCapacityBlocked = false,
                BlockedChannel = null,
                BlockedReason = "none"
            };
        }
    }
}
