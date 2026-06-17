namespace IIoT.Edge.Application.Abstractions.Updates;

public interface IEdgeUpdateConfigurationProvider
{
    EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target);

    EdgeReleaseOptions ResolveReleaseOptions();
}

public interface IEdgeUpdateConfigInitializer
{
    void EnsureConfigExists();

    bool TrySyncUpdateSource(string updateSource);
}

public interface IEdgeUpdateDeviceSessionClient
{
    Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
        EdgeUpdateCloudApiOptions options,
        CancellationToken cancellationToken = default);
}

public interface IEdgeUpdateCatalogClient
{
    Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeReleaseOptions releaseOptions,
        CancellationToken cancellationToken = default);
}

public interface IEdgeVersionReporter
{
    Task<EdgeVersionReportResult> ReportVersionAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeReleaseOptions releaseOptions,
        EdgeUpdateTarget target,
        string hostVersion,
        string hostApiVersion,
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
        IReadOnlyList<string> enabledPlugins,
        CancellationToken cancellationToken = default);
}

public interface IEdgeInstalledPluginCatalog
{
    IReadOnlyList<EdgeInstalledPlugin> LoadInstalledPlugins(EdgeUpdateTarget target);
}

public interface IEdgeProfileModuleConfigurationStore
{
    IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target);

    void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds);
}

public interface IEdgePluginPackageInstaller
{
    Task<EdgePluginInstallResult> InstallAsync(
        EdgeUpdateTarget target,
        EdgePluginVersionRelease release,
        EdgeUpdateCloudApiOptions cloudOptions,
        string hostVersion,
        string hostApiVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEdgeHostUpdateService
{
    Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
        EdgeHostVersionRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IEdgeVersionCompatibilityPolicy
{
    bool IsReleaseCompatible(
        EdgePluginVersionRelease release,
        string hostVersion,
        string hostApiVersion,
        out string? issue);
}

public interface IEdgeReleaseService
{
    Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken = default);

    Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
        EdgeUpdateTarget target,
        string moduleId,
        string version,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
        EdgeUpdateTarget target,
        string version,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
        EdgeUpdateTarget target,
        EdgeVersionSelection selection,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken = default);
}
