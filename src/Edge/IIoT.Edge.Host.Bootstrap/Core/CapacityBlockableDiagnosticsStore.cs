using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public abstract class CapacityBlockableDiagnosticsStore<TSnapshot, TRuntimeState>
{
    private readonly object _sync = new();
    private TSnapshot _snapshot;

    protected CapacityBlockableDiagnosticsStore(TSnapshot initialSnapshot)
    {
        _snapshot = initialSnapshot;
    }

    protected TSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    protected void UpdateSnapshot(Func<TSnapshot, TSnapshot> update)
    {
        lock (_sync)
        {
            _snapshot = update(_snapshot);
        }
    }

    protected void SetRuntimeStateCore(
        TRuntimeState state,
        Func<TSnapshot, TRuntimeState> getState,
        Func<TSnapshot, TRuntimeState, TSnapshot> setState)
    {
        lock (_sync)
        {
            if (EqualityComparer<TRuntimeState>.Default.Equals(getState(_snapshot), state))
            {
                return;
            }

            _snapshot = setState(_snapshot, state);
        }
    }

    protected void MarkCapacityBlockedCore(
        CapacityBlockedChannel channel,
        string blockedReason,
        DateTime? occurredAt,
        Func<TSnapshot, CapacityBlockedChannel, string, DateTime, TSnapshot> setCapacityBlocked)
    {
        var reason = string.IsNullOrWhiteSpace(blockedReason) ? "unknown" : blockedReason;
        var blockTime = occurredAt ?? DateTime.UtcNow;
        lock (_sync)
        {
            _snapshot = setCapacityBlocked(_snapshot, channel, reason, blockTime);
        }
    }

    protected void ClearCapacityBlockedCore(
        Func<TSnapshot, bool> isCapacityBlocked,
        Func<TSnapshot, TSnapshot> clearCapacityBlocked)
    {
        lock (_sync)
        {
            if (!isCapacityBlocked(_snapshot))
            {
                return;
            }

            _snapshot = clearCapacityBlocked(_snapshot);
        }
    }
}
