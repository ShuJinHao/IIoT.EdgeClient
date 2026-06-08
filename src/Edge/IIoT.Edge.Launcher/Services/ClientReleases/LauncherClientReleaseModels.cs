using System.Text.Json.Serialization;

namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherClientReleaseOptions(
    string Channel,
    string TargetRuntime);

public sealed record LauncherCloudApiOptions(
    string BaseUrl,
    int TimeoutSeconds,
    string ClientCode,
    string BootstrapSecret,
    string DeviceInstancePath,
    string ClientReleaseCatalogTemplate,
    string ClientVersionReportPath);

public sealed record LauncherCloudApiConfigurationResult(
    bool Success,
    LauncherCloudApiOptions? Options = null,
    string? ErrorMessage = null)
{
    public static LauncherCloudApiConfigurationResult Succeeded(LauncherCloudApiOptions options)
        => new(true, options);

    public static LauncherCloudApiConfigurationResult Failed(string errorMessage)
        => new(false, ErrorMessage: errorMessage);
}

public sealed record LauncherEdgeDeviceSession(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? UploadAccessToken);

public sealed record LauncherEdgeCloudOperationResult<T>(
    bool Success,
    T? Value = default,
    string? ErrorMessage = null)
{
    public static LauncherEdgeCloudOperationResult<T> Succeeded(T value)
        => new(true, value);

    public static LauncherEdgeCloudOperationResult<T> Failed(string errorMessage)
        => new(false, ErrorMessage: errorMessage);
}

public sealed record LauncherClientReleaseCatalog(
    int CatalogSchemaVersion,
    string Channel,
    string? TargetRuntime,
    LauncherClientHostRelease? LatestHost,
    IReadOnlyList<LauncherClientHostRelease> HostReleases,
    IReadOnlyList<LauncherClientPluginRelease> PluginReleases,
    DateTime GeneratedAtUtc);

public sealed record LauncherClientHostRelease(
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

public sealed record LauncherClientPluginRelease(
    Guid Id,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
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

public sealed record LauncherInstalledPlugin(
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

public enum LauncherPluginUpdateState
{
    NotInstalled,
    Latest,
    UpdateAvailable,
    InstalledNewer,
    Incompatible
}

public sealed record LauncherPluginUpdatePlan(
    LauncherClientPluginRelease Release,
    LauncherInstalledPlugin? InstalledPlugin,
    LauncherPluginUpdateState State,
    string? CompatibilityIssue)
{
    public bool CanInstallOrUpdate => State is LauncherPluginUpdateState.NotInstalled or LauncherPluginUpdateState.UpdateAvailable;
}

public enum LauncherClientReleaseCheckState
{
    NotConfigured,
    BootstrapFailed,
    CatalogUnavailable,
    Succeeded,
    Failed
}

public sealed record LauncherClientReleaseCheckResult(
    LauncherClientReleaseCheckState State,
    string Channel,
    string TargetRuntime,
    string HostVersion,
    string HostApiVersion,
    string? LatestHostVersion,
    IReadOnlyList<LauncherPluginUpdatePlan> Plugins,
    string? ErrorMessage = null)
{
    public bool Success => State == LauncherClientReleaseCheckState.Succeeded;
}

public sealed record LauncherPluginInstallResult(
    bool Success,
    IReadOnlyList<string> InstalledModuleIds,
    string? ErrorMessage = null)
{
    public static LauncherPluginInstallResult Succeeded(IReadOnlyList<string> installedModuleIds)
        => new(true, installedModuleIds);

    public static LauncherPluginInstallResult Failed(string errorMessage)
        => new(false, [], errorMessage);
}

public sealed record LauncherVersionReportResult(
    bool Success,
    string? ErrorMessage = null)
{
    public static LauncherVersionReportResult Succeeded()
        => new(true);

    public static LauncherVersionReportResult Failed(string errorMessage)
        => new(false, errorMessage);
}

internal sealed class LauncherPluginManifest
{
    [JsonPropertyName("moduleId")]
    public string ModuleId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("hostApiVersion")]
    public string HostApiVersion { get; set; } = string.Empty;

    [JsonPropertyName("minHostVersion")]
    public string MinHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("maxHostVersion")]
    public string MaxHostVersion { get; set; } = string.Empty;

    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    [JsonPropertyName("entryType")]
    public string EntryType { get; set; } = string.Empty;

    [JsonPropertyName("supportedProcessType")]
    public string SupportedProcessType { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = [];
}
