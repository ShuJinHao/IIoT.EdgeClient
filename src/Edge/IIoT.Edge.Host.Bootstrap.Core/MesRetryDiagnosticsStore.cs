using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class MesRetryDiagnosticsStore : IMesRetryDiagnosticsStore
{
    private readonly object _sync = new();
    private string? _blockedProcessType;
    private MesRetryDiagnosticsSnapshot _snapshot = new(
        MesRetryRuntimeState.Idle,
        IsCapacityBlocked: false,
        BlockedChannel: null,
        BlockedReason: "none",
        LastCapacityBlockAt: null);

    public MesRetryDiagnosticsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void SetRuntimeState(MesRetryRuntimeState state)
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
