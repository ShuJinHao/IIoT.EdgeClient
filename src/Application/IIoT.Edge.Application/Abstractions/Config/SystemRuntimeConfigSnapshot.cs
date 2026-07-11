namespace IIoT.Edge.Application.Abstractions.Config;

public sealed record SystemRuntimeConfigSnapshot(
    bool MesUploadEnabled,
    bool SystemCloudEnabled,
    TimeSpan OnlineHeartbeatInterval,
    TimeSpan CloudSyncInterval,
    TimeSpan RuntimeHeartbeatInterval)
{
    public static SystemRuntimeConfigSnapshot Default { get; } = new(
        MesUploadEnabled: true,
        SystemCloudEnabled: false,
        OnlineHeartbeatInterval: TimeSpan.FromSeconds(60),
        CloudSyncInterval: TimeSpan.FromSeconds(60),
        RuntimeHeartbeatInterval: TimeSpan.FromSeconds(60));
}
