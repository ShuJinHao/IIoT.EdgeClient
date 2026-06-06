namespace IIoT.Edge.Launcher.Services;

public enum LauncherUpdateCheckState
{
    NotConfigured,
    NotInstalled,
    NoUpdate,
    UpdateAvailable,
    PendingRestart,
    Failed
}

public sealed record LauncherUpdateCheckResult(
    LauncherUpdateCheckState State,
    string? CurrentVersion = null,
    string? TargetVersion = null,
    string? ReleaseNotes = null,
    string? ErrorMessage = null)
{
    public bool HasUpdate => State is LauncherUpdateCheckState.UpdateAvailable or LauncherUpdateCheckState.PendingRestart;
}

public sealed record LauncherUpdateApplyResult(
    bool Started,
    string? ErrorMessage = null);

public interface ILauncherUpdateService
{
    Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<LauncherUpdateApplyResult> DownloadAndApplyUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
