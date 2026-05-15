namespace IIoT.Edge.Launcher.Models;

public sealed record LauncherAccountRecord(
    string UserName,
    string DisplayName,
    string PasswordHash,
    bool IsEnabled);
