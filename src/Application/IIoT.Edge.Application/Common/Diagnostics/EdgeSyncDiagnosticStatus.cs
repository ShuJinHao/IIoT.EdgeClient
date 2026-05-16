using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Common.Diagnostics;

/// <summary>
/// 云端同步状态在界面上的归类。
/// 枚举顺序不代表优先级，优先级由 <see cref="IEdgeSyncDiagnosticStatusClassifier"/> 集中判断。
/// </summary>
public enum CloudSyncDiagnosticStatus
{
    PersistenceFaulted,
    CapacityBlocked,
    WaitingHeartbeat,
    Ready,
    WaitingRecovery,
    Blocked
}

/// <summary>
/// MES 同步状态在界面上的归类。
/// 枚举顺序不代表优先级，优先级由 <see cref="IEdgeSyncDiagnosticStatusClassifier"/> 集中判断。
/// </summary>
public enum MesSyncDiagnosticStatus
{
    PersistenceFaulted,
    CapacityBlocked,
    WaitingHeartbeat,
    Retrying,
    Backoff,
    LastFailed,
    Idle
}

public interface IEdgeSyncDiagnosticStatusClassifier
{
    CloudSyncDiagnosticStatus ClassifyCloud(CloudSyncDiagnosticsSnapshot snapshot);

    MesSyncDiagnosticStatus ClassifyMes(MesSyncDiagnosticsSnapshot snapshot);
}

/// <summary>
/// 集中维护 Cloud/MES 同步诊断的显示状态优先级，避免页脚、诊断页和监控页各写一套判断。
/// </summary>
public sealed class EdgeSyncDiagnosticStatusClassifier : IEdgeSyncDiagnosticStatusClassifier
{
    public CloudSyncDiagnosticStatus ClassifyCloud(CloudSyncDiagnosticsSnapshot snapshot)
        => snapshot switch
        {
            { IsPersistenceFaulted: true } => CloudSyncDiagnosticStatus.PersistenceFaulted,
            { IsCapacityBlocked: true } => CloudSyncDiagnosticStatus.CapacityBlocked,
            { Heartbeat: { IsReady: false } } => CloudSyncDiagnosticStatus.WaitingHeartbeat,
            { GateState: EdgeUploadGateState.Ready } => CloudSyncDiagnosticStatus.Ready,
            { IsPausedWaitingForRecovery: true } => CloudSyncDiagnosticStatus.WaitingRecovery,
            _ => CloudSyncDiagnosticStatus.Blocked
        };

    public MesSyncDiagnosticStatus ClassifyMes(MesSyncDiagnosticsSnapshot snapshot)
        => snapshot switch
        {
            { IsPersistenceFaulted: true } => MesSyncDiagnosticStatus.PersistenceFaulted,
            { IsCapacityBlocked: true } => MesSyncDiagnosticStatus.CapacityBlocked,
            { Heartbeat: { IsReady: false } } => MesSyncDiagnosticStatus.WaitingHeartbeat,
            { RuntimeState: MesRetryRuntimeState.Retrying } => MesSyncDiagnosticStatus.Retrying,
            { RuntimeState: MesRetryRuntimeState.Backoff } => MesSyncDiagnosticStatus.Backoff,
            { RuntimeState: MesRetryRuntimeState.LastFailed } => MesSyncDiagnosticStatus.LastFailed,
            _ => MesSyncDiagnosticStatus.Idle
        };
}
