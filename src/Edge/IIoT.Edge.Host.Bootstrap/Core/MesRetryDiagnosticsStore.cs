using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Shell.Core;

public sealed class MesRetryDiagnosticsStore
    : CapacityBlockableDiagnosticsStore<MesRetryDiagnosticsSnapshot, MesRetryRuntimeState>,
        IMesRetryDiagnosticsStore
{
    public MesRetryDiagnosticsStore()
        : base(new MesRetryDiagnosticsSnapshot(
            MesRetryRuntimeState.Idle,
            IsCapacityBlocked: false,
            BlockedChannel: null,
            BlockedReason: "none",
            LastCapacityBlockAt: null))
    {
    }

    public MesRetryDiagnosticsSnapshot Snapshot => GetSnapshot();

    public void SetRuntimeState(MesRetryRuntimeState state)
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
