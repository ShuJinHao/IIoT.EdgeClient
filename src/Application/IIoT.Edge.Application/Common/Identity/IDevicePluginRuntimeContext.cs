namespace IIoT.Edge.Application.Common.Identity;

/// <summary>
/// The installed device-plugin identity injected by a validated binding.
/// Business routing must use this context instead of deriving ProcessType from ModuleId.
/// </summary>
public interface IDevicePluginRuntimeContext
{
    DevicePluginRuntimeIdentity Current { get; }
}

public sealed record DevicePluginRuntimeIdentity(
    int SchemaVersion,
    string GenerationId,
    string ClientCode,
    string ProcessType,
    string ModuleId,
    string PluginVersion,
    string PackageSha256)
{
    public bool IsV3 => SchemaVersion >= 3;

    public static DevicePluginRuntimeIdentity Legacy { get; } = new(
        2,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}
