namespace IIoT.Edge.Launcher.Models;

public sealed record LauncherProfileDefinition(
    string ProfileId,
    string DisplayName,
    string Description,
    string? ImagePath,
    string MachineProfile,
    string ExecutablePath,
    string IconKind,
    string AccentColor)
{
    public string ClientCode { get; init; } = string.Empty;

    public string ProcessType { get; init; } = string.Empty;

    public Guid? ProcessId { get; init; }

    public string PluginVersion { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;

    public string MachineConfigPath { get; init; } = string.Empty;

    public IReadOnlyList<string> ExpectedModuleIds { get; init; } = [];

    public string ActivationModuleId { get; init; } = string.Empty;

    public string ActivationPluginDirectory { get; init; } = string.Empty;

    public string PluginDisplayPath { get; init; } = string.Empty;

    public string DataDisplayPath { get; init; } = string.Empty;
}
