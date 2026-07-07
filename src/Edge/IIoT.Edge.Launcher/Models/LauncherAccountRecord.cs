namespace IIoT.Edge.Launcher.Models;

public sealed record LauncherAccountRecord(
    string UserName,
    string DisplayName,
    string PasswordHash,
    bool IsEnabled,
    int AccessFailedCount = 0,
    DateTimeOffset? LockoutUntilUtc = null);
