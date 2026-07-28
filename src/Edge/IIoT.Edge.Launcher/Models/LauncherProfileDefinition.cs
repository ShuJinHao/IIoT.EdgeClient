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
    public IReadOnlyList<string> ExpectedModuleIds { get; init; } = [];

    public string PluginDisplayPath { get; init; } = string.Empty;

    public string DataDisplayPath { get; init; } = string.Empty;
}
