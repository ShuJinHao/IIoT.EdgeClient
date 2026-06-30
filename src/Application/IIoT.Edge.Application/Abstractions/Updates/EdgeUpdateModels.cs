namespace IIoT.Edge.Application.Abstractions.Updates;

public sealed record EdgeUpdateTarget(
    string MachineProfile,
    string HostDirectory,
    string HostExecutablePath);

public sealed record EdgeReleaseOptions(
    string Channel,
    string TargetRuntime);

public sealed record EdgeUpdateCloudApiOptions(
    string BaseUrl,
    int TimeoutSeconds,
    string ClientCode,
    string BootstrapSecret,
    string DeviceInstancePath,
    string ClientReleaseCatalogTemplate,
    string ClientVersionReportPath,
    string RuntimeHeartbeatPath);

public sealed record EdgeUpdateConfigurationResult(
    bool Success,
    EdgeUpdateCloudApiOptions? Options = null,
    string? ErrorMessage = null)
{
    public static EdgeUpdateConfigurationResult Succeeded(EdgeUpdateCloudApiOptions options)
        => new(true, options);

    public static EdgeUpdateConfigurationResult Failed(string errorMessage)
        => new(false, ErrorMessage: errorMessage);
}

public sealed record EdgeUpdateDeviceSession(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? AccessToken);

public sealed record EdgeUpdateOperationResult<T>(
    bool Success,
    T? Value = default,
    string? ErrorMessage = null)
{
    public static EdgeUpdateOperationResult<T> Succeeded(T value)
        => new(true, value);

    public static EdgeUpdateOperationResult<T> Failed(string errorMessage)
        => new(false, ErrorMessage: errorMessage);
}

public sealed record EdgeReleaseCatalog(
    int CatalogSchemaVersion,
    string Channel,
    string? TargetRuntime,
    EdgeHostReleaseComponent Host,
    IReadOnlyList<EdgePluginReleaseComponent> Plugins,
    DateTime GeneratedAtUtc,
    string? HostUpdateSource = null);

public sealed record EdgeHostReleaseComponent(
    string ComponentKind,
    string DisplayName,
    IReadOnlyList<EdgeHostVersionEntry> Versions);

public sealed record EdgePluginReleaseComponent(
    string ComponentKind,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    IReadOnlyList<EdgePluginVersionEntry> Versions);

public sealed record EdgeHostVersionEntry(
    Guid Id,
    string Channel,
    string Version,
    string HostApiVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string Status,
    string? Signature,
    string? Publisher,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc);

public sealed record EdgePluginVersionEntry(
    Guid Id,
    string Channel,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    IReadOnlyList<string> Dependencies,
    string Status,
    string? Signature,
    string? Publisher,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc);

public sealed record EdgeHostVersionRelease(
    EdgeHostVersionEntry Version);

public sealed record EdgePluginVersionRelease(
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    EdgePluginVersionEntry Version)
{
    public Guid Id => Version.Id;
    public string Channel => Version.Channel;
    public string PackageVersion => Version.Version;
    public string HostApiVersion => Version.HostApiVersion;
    public string MinHostVersion => Version.MinHostVersion;
    public string MaxHostVersion => Version.MaxHostVersion;
    public string TargetRuntime => Version.TargetRuntime;
    public string? TargetFramework => Version.TargetFramework;
    public string DownloadUrl => Version.DownloadUrl;
    public string Sha256 => Version.Sha256;
    public long PackageSize => Version.PackageSize;
    public string? ReleaseNotes => Version.ReleaseNotes;
    public IReadOnlyList<string> Dependencies => Version.Dependencies;
    public string Status => Version.Status;
    public string? Signature => Version.Signature;
    public string? Publisher => Version.Publisher;
}

public sealed record EdgeInstalledPlugin(
    string ModuleId,
    string ProcessType,
    string DisplayName,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    IReadOnlyList<string> Dependencies,
    string ManifestPath,
    string PluginDirectory);

public enum EdgeComponentKind
{
    Host,
    Plugin
}

public enum EdgeVersionStatus
{
    NotInstalled,
    Current,
    Newer,
    Older,
    InstalledNewer,
    Incompatible,
    Deprecated
}

public sealed record EdgeVersionOption(
    string Version,
    EdgeVersionStatus Status,
    bool CanApply,
    string? CompatibilityIssue,
    EdgeHostVersionRelease? HostRelease = null,
    EdgePluginVersionRelease? PluginRelease = null);

public sealed record EdgeComponentVersionPlan(
    EdgeComponentKind ComponentKind,
    string ModuleId,
    string DisplayName,
    string? CurrentVersion,
    IReadOnlyList<EdgeVersionOption> Versions);

public enum EdgeReleaseCatalogState
{
    NotConfigured,
    BootstrapFailed,
    CatalogUnavailable,
    Succeeded,
    Failed
}

public sealed record EdgeReleaseCatalogResult(
    EdgeReleaseCatalogState State,
    string Channel,
    string TargetRuntime,
    string HostVersion,
    string HostApiVersion,
    IReadOnlyList<EdgeComponentVersionPlan> Components,
    string? ErrorMessage = null)
{
    public bool Success => State == EdgeReleaseCatalogState.Succeeded;
}

public sealed record EdgeVersionSelection(
    string? HostVersion,
    IReadOnlyDictionary<string, string> PluginVersions);

public sealed record EdgePluginInstallResult(
    bool Success,
    IReadOnlyList<string> InstalledModuleIds,
    string? ErrorMessage = null)
{
    public static EdgePluginInstallResult Succeeded(IReadOnlyList<string> installedModuleIds)
        => new(true, installedModuleIds);

    public static EdgePluginInstallResult Failed(string errorMessage)
        => new(false, [], errorMessage);
}

public sealed record EdgeVersionReportResult(
    bool Success,
    string? ErrorMessage = null)
{
    public static EdgeVersionReportResult Succeeded()
        => new(true);

    public static EdgeVersionReportResult Failed(string errorMessage)
        => new(false, errorMessage);
}

public enum EdgeRuntimeHeartbeatStatus
{
    Starting,
    Running,
    Stopping,
    Stopped
}

public sealed record EdgeRuntimeHeartbeatReport(
    string RuntimeInstanceId,
    string? MachineProfile,
    string HostVersion,
    string HostApiVersion,
    EdgeRuntimeHeartbeatStatus Status,
    DateTime StartedAtUtc,
    DateTime ReportedAtUtc);

public sealed record EdgeRuntimeHeartbeatReportResult(
    bool Success,
    string? ErrorMessage = null)
{
    public static EdgeRuntimeHeartbeatReportResult Succeeded()
        => new(true);

    public static EdgeRuntimeHeartbeatReportResult Failed(string errorMessage)
        => new(false, errorMessage);
}

public enum EdgeHostUpdateCheckState
{
    NotConfigured,
    NotInstalled,
    NoUpdate,
    UpdateAvailable,
    PendingRestart,
    Failed
}

public sealed record EdgeHostUpdateCheckResult(
    EdgeHostUpdateCheckState State,
    string? CurrentVersion = null,
    string? TargetVersion = null,
    string? ReleaseNotes = null,
    string? ErrorMessage = null)
{
    public bool HasUpdate => State is EdgeHostUpdateCheckState.UpdateAvailable or EdgeHostUpdateCheckState.PendingRestart;
}

public sealed record EdgeHostUpdateApplyResult(
    bool Started,
    string? ErrorMessage = null);
