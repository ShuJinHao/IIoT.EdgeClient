namespace IIoT.Edge.Application.Abstractions.Config;

public sealed record SystemRuntimeConfigSnapshot(
    bool MesUploadEnabled,
    TimeSpan OnlineHeartbeatInterval,
    TimeSpan CloudSyncInterval)
{
    public static SystemRuntimeConfigSnapshot Default { get; } = new(
        MesUploadEnabled: true,
        OnlineHeartbeatInterval: TimeSpan.FromSeconds(60),
        CloudSyncInterval: TimeSpan.FromSeconds(60));
}
