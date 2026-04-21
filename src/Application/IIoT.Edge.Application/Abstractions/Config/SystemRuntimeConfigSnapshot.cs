namespace IIoT.Edge.Application.Abstractions.Config;

public sealed record SystemRuntimeConfigSnapshot(
    string? MesBaseUrl,
    bool MesUploadEnabled,
    TimeSpan OnlineHeartbeatInterval,
    TimeSpan CloudSyncInterval)
{
    public static SystemRuntimeConfigSnapshot Default { get; } = new(
        MesBaseUrl: null,
        MesUploadEnabled: true,
        OnlineHeartbeatInterval: TimeSpan.FromSeconds(60),
        CloudSyncInterval: TimeSpan.FromSeconds(60));
}
