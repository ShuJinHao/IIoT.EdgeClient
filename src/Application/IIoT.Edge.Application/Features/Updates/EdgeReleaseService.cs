using System.Reflection;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Application.Features.Updates;

public sealed class EdgeReleaseService : IEdgeReleaseService
{
    private readonly IEdgeUpdateConfigurationProvider _configurationProvider;
    private readonly IEdgeUpdateDeviceSessionClient _deviceSessionClient;
    private readonly IEdgeUpdateCatalogClient _catalogClient;
    private readonly IEdgeVersionReporter _versionReporter;
    private readonly IEdgeInstalledPluginCatalog _installedPluginCatalog;
    private readonly IEdgeProfileModuleConfigurationStore _profileModuleConfigurationStore;
    private readonly IEdgePluginPackageInstaller _packageInstaller;
    private readonly IEdgeHostUpdateService _hostUpdateService;
    private readonly IEdgeUpdateConfigInitializer _updateConfigInitializer;
    private readonly IEdgeVersionCompatibilityPolicy _compatibilityPolicy;

    public EdgeReleaseService(
        IEdgeUpdateConfigurationProvider configurationProvider,
        IEdgeUpdateDeviceSessionClient deviceSessionClient,
        IEdgeUpdateCatalogClient catalogClient,
        IEdgeVersionReporter versionReporter,
        IEdgeInstalledPluginCatalog installedPluginCatalog,
        IEdgeProfileModuleConfigurationStore profileModuleConfigurationStore,
        IEdgePluginPackageInstaller packageInstaller,
        IEdgeHostUpdateService hostUpdateService,
        IEdgeUpdateConfigInitializer updateConfigInitializer,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _deviceSessionClient = deviceSessionClient ?? throw new ArgumentNullException(nameof(deviceSessionClient));
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _versionReporter = versionReporter ?? throw new ArgumentNullException(nameof(versionReporter));
        _installedPluginCatalog = installedPluginCatalog ?? throw new ArgumentNullException(nameof(installedPluginCatalog));
        _profileModuleConfigurationStore = profileModuleConfigurationStore ?? throw new ArgumentNullException(nameof(profileModuleConfigurationStore));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _hostUpdateService = hostUpdateService ?? throw new ArgumentNullException(nameof(hostUpdateService));
        _updateConfigInitializer = updateConfigInitializer ?? throw new ArgumentNullException(nameof(updateConfigInitializer));
        _compatibilityPolicy = compatibilityPolicy ?? throw new ArgumentNullException(nameof(compatibilityPolicy));
    }

    public async Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var releaseOptions = _configurationProvider.ResolveReleaseOptions();
        var hostVersion = ResolveHostVersion(target);
        var hostApiVersion = EdgeClientHostRuntime.HostApiVersion;
        var installedPlugins = _installedPluginCatalog.LoadInstalledPlugins(target);
        var configuration = _configurationProvider.Resolve(target);
        if (!configuration.Success || configuration.Options is null)
        {
            return CreateResult(
                EdgeReleaseCatalogState.NotConfigured,
                releaseOptions,
                hostVersion,
                hostApiVersion,
                BuildLocalVersionPlans(installedPlugins, hostVersion),
                configuration.ErrorMessage);
        }

        var session = await _deviceSessionClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return CreateResult(
                EdgeReleaseCatalogState.BootstrapFailed,
                releaseOptions,
                hostVersion,
                hostApiVersion,
                BuildLocalVersionPlans(installedPlugins, hostVersion),
                session.ErrorMessage);
        }

        var catalog = await LoadCatalogAsync(
            configuration.Options,
            session.Value,
            releaseOptions,
            cancellationToken).ConfigureAwait(false);
        if (!catalog.Success || catalog.Value is null)
        {
            return CreateResult(
                EdgeReleaseCatalogState.CatalogUnavailable,
                releaseOptions,
                hostVersion,
                hostApiVersion,
                BuildLocalVersionPlans(installedPlugins, hostVersion),
                catalog.ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(catalog.Value.HostUpdateSource))
        {
            _updateConfigInitializer.TrySyncUpdateSource(catalog.Value.HostUpdateSource);
        }

        var enabledPlugins = _profileModuleConfigurationStore.ReadEnabledModules(target);
        _ = await _versionReporter
            .ReportVersionAsync(
                configuration.Options,
                session.Value,
                releaseOptions,
                target,
                hostVersion,
                hostApiVersion,
                installedPlugins,
                enabledPlugins,
                cancellationToken)
            .ConfigureAwait(false);

        return CreateResult(
            EdgeReleaseCatalogState.Succeeded,
            releaseOptions,
            hostVersion,
            hostApiVersion,
            BuildVersionPlans(catalog.Value, installedPlugins, hostVersion, hostApiVersion, _compatibilityPolicy, enabledPlugins));
    }

    public async Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
        EdgeUpdateTarget target,
        string moduleId,
        string version,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var context = await CreateOperationContextAsync(target, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return EdgePluginInstallResult.Failed(context.Error);
        }

        var enabledModuleIssue = ValidateTargetModuleEnabled(target, moduleId);
        if (enabledModuleIssue is not null)
        {
            return EdgePluginInstallResult.Failed(enabledModuleIssue);
        }

        var releases = FlattenPluginVersions(context.Catalog!);
        var key = moduleId.Trim();
        if (!releases.TryGetValue(key, out var moduleReleases)
            || moduleReleases.FirstOrDefault(release =>
                string.Equals(release.PackageVersion, version.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } targetRelease)
        {
            return EdgePluginInstallResult.Failed($"Cloud catalog 中未找到插件 {moduleId} 的版本 {version}。");
        }

        var selectedByModule = releases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.First(),
            StringComparer.OrdinalIgnoreCase);
        selectedByModule[targetRelease.ModuleId] = targetRelease;
        return await InstallPluginReleasesAsync(
            target,
            context,
            targetRelease,
            selectedByModule,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
        EdgeUpdateTarget target,
        string version,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var context = await CreateOperationContextAsync(target, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return new EdgeHostUpdateApplyResult(false, context.Error);
        }

        var release = context.Catalog!.Host.Versions
            .FirstOrDefault(entry => string.Equals(entry.Version, version.Trim(), StringComparison.OrdinalIgnoreCase));
        return release is null
            ? new EdgeHostUpdateApplyResult(false, $"Cloud catalog 中未找到宿主版本 {version}。")
            : await _hostUpdateService
                .ApplyVersionAsync(new EdgeHostVersionRelease(release), progress, cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
        EdgeUpdateTarget target,
        EdgeVersionSelection selection,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(selection);

        var context = await CreateOperationContextAsync(target, cancellationToken).ConfigureAwait(false);
        if (context.Error is not null)
        {
            return EdgePluginInstallResult.Failed(context.Error);
        }

        foreach (var item in selection.PluginVersions.Keys)
        {
            var enabledModuleIssue = ValidateTargetModuleEnabled(target, item);
            if (enabledModuleIssue is not null)
            {
                return EdgePluginInstallResult.Failed(enabledModuleIssue);
            }
        }

        var releases = FlattenPluginVersions(context.Catalog!);
        var selectedByModule = releases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.First(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in selection.PluginVersions)
        {
            if (!releases.TryGetValue(item.Key, out var moduleReleases)
                || moduleReleases.FirstOrDefault(release =>
                    string.Equals(release.PackageVersion, item.Value, StringComparison.OrdinalIgnoreCase)) is not { } selected)
            {
                return EdgePluginInstallResult.Failed($"Cloud catalog 中未找到插件 {item.Key} 的版本 {item.Value}。");
            }

            selectedByModule[item.Key] = selected;
        }

        var installedModuleIds = new List<string>();
        var steps = selection.PluginVersions.Count + (string.IsNullOrWhiteSpace(selection.HostVersion) ? 0 : 1);
        var stepBase = 0;
        foreach (var item in selection.PluginVersions)
        {
            var release = selectedByModule[item.Key];
            var result = await InstallPluginReleasesAsync(
                target,
                context,
                release,
                selectedByModule,
                CreateStepProgress(progress, stepBase, steps),
                cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                return result;
            }

            installedModuleIds.AddRange(result.InstalledModuleIds);
            stepBase += 100 / Math.Max(steps, 1);
        }

        if (!string.IsNullOrWhiteSpace(selection.HostVersion))
        {
            var hostResult = await ApplyHostVersionAsync(
                target,
                selection.HostVersion,
                CreateStepProgress(progress, stepBase, steps),
                cancellationToken).ConfigureAwait(false);
            if (!hostResult.Started)
            {
                return EdgePluginInstallResult.Failed(hostResult.ErrorMessage ?? "宿主版本应用失败。");
            }
        }

        progress?.Report(100);
        return EdgePluginInstallResult.Succeeded(installedModuleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public async Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var configuration = _configurationProvider.Resolve(target);
        if (!configuration.Success || configuration.Options is null)
        {
            return EdgeVersionReportResult.Failed(configuration.ErrorMessage ?? "CloudApi 配置不可用。");
        }

        var session = await _deviceSessionClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return EdgeVersionReportResult.Failed(session.ErrorMessage ?? "Cloud bootstrap 失败。");
        }

        return await _versionReporter
            .ReportVersionAsync(
                configuration.Options,
                session.Value,
                _configurationProvider.ResolveReleaseOptions(),
                target,
                ResolveHostVersion(target),
                EdgeClientHostRuntime.HostApiVersion,
                _installedPluginCatalog.LoadInstalledPlugins(target),
                _profileModuleConfigurationStore.ReadEnabledModules(target),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static IReadOnlyList<EdgeComponentVersionPlan> BuildVersionPlans(
        EdgeReleaseCatalog catalog,
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
        string hostVersion,
        string hostApiVersion,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy,
        IReadOnlyList<string>? enabledModuleIds = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(installedPlugins);
        ArgumentNullException.ThrowIfNull(compatibilityPolicy);

        var plans = new List<EdgeComponentVersionPlan>
        {
            new(
                EdgeComponentKind.Host,
                "Host",
                string.IsNullOrWhiteSpace(catalog.Host.DisplayName) ? "Edge Host" : catalog.Host.DisplayName,
                hostVersion,
                catalog.Host.Versions
                    .Select(version => new EdgeVersionOption(
                        version.Version,
                        ResolveHostVersionStatus(hostVersion, version),
                        !string.Equals(hostVersion, version.Version, StringComparison.OrdinalIgnoreCase),
                        null,
                        HostRelease: new EdgeHostVersionRelease(version)))
                    .ToList())
        };

        var installedByModule = installedPlugins.ToDictionary(
            static plugin => plugin.ModuleId,
            StringComparer.OrdinalIgnoreCase);
        var plannedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledModuleSet = CreateEnabledModuleSet(enabledModuleIds);
        foreach (var component in catalog.Plugins.OrderBy(static plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsModuleVisible(component.ModuleId, enabledModuleSet))
            {
                continue;
            }

            plannedModules.Add(component.ModuleId);
            installedByModule.TryGetValue(component.ModuleId, out var installed);
            var releases = component.Versions
                .Select(version => new EdgePluginVersionRelease(
                    component.ModuleId,
                    string.IsNullOrWhiteSpace(component.DisplayName) ? component.ModuleId : component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    version))
                .ToList();
            plans.Add(new EdgeComponentVersionPlan(
                EdgeComponentKind.Plugin,
                component.ModuleId,
                string.IsNullOrWhiteSpace(component.DisplayName) ? component.ModuleId : component.DisplayName,
                installed?.Version,
                releases.Select(release =>
                {
                    var status = ResolvePluginVersionStatus(
                        release,
                        installed,
                        hostVersion,
                        hostApiVersion,
                        compatibilityPolicy,
                        out var issue);
                    return new EdgeVersionOption(
                        release.PackageVersion,
                        status,
                        status is EdgeVersionStatus.NotInstalled or EdgeVersionStatus.Newer or EdgeVersionStatus.Older,
                        issue,
                        PluginRelease: release);
                }).ToList()));
        }

        foreach (var installed in installedPlugins
                     .Where(plugin => !plannedModules.Contains(plugin.ModuleId)
                                      && IsModuleVisible(plugin.ModuleId, enabledModuleSet))
                     .OrderBy(static plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            plans.Add(BuildInstalledPluginPlan(installed));
        }

        return plans;
    }

    private string? ValidateTargetModuleEnabled(EdgeUpdateTarget target, string moduleId)
    {
        var enabledModules = _profileModuleConfigurationStore.ReadEnabledModules(target);
        if (enabledModules.Count == 0)
        {
            return $"当前工序未配置启用插件，不能安装或更新插件 {moduleId}。";
        }

        return enabledModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase)
            ? null
            : $"插件 {moduleId} 不属于当前工序，不能在这里安装或更新。";
    }

    private static HashSet<string>? CreateEnabledModuleSet(IReadOnlyList<string>? enabledModuleIds)
        => enabledModuleIds is null
            ? null
            : enabledModuleIds
                .Where(static moduleId => !string.IsNullOrWhiteSpace(moduleId))
                .Select(static moduleId => moduleId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsModuleVisible(string moduleId, IReadOnlySet<string>? enabledModuleSet)
        => enabledModuleSet is null || enabledModuleSet.Contains(moduleId);

    public static IReadOnlyList<EdgeComponentVersionPlan> BuildLocalVersionPlans(
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
        string hostVersion)
    {
        ArgumentNullException.ThrowIfNull(installedPlugins);

        var plans = new List<EdgeComponentVersionPlan>
        {
            new(
                EdgeComponentKind.Host,
                "Host",
                "Edge Host",
                hostVersion,
                [])
        };

        foreach (var installed in installedPlugins
                     .OrderBy(static plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            plans.Add(BuildInstalledPluginPlan(installed));
        }

        return plans;
    }

    private static EdgeComponentVersionPlan BuildInstalledPluginPlan(EdgeInstalledPlugin installed)
        => new(
            EdgeComponentKind.Plugin,
            installed.ModuleId,
            string.IsNullOrWhiteSpace(installed.DisplayName) ? installed.ModuleId : installed.DisplayName,
            installed.Version,
            []);

    private async Task<EdgePluginInstallResult> InstallPluginReleasesAsync(
        EdgeUpdateTarget target,
        OperationContext context,
        EdgePluginVersionRelease targetRelease,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> selectedByModule,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var ordered = ResolveInstallOrder(targetRelease, selectedByModule, out var dependencyIssue);
        if (dependencyIssue is not null)
        {
            return EdgePluginInstallResult.Failed(dependencyIssue);
        }

        var installedModuleIds = new List<string>();
        var stepBase = 0;
        foreach (var release in ordered)
        {
            if (!_compatibilityPolicy.IsReleaseCompatible(
                    release,
                    context.HostVersion,
                    context.HostApiVersion,
                    out var compatibilityIssue))
            {
                return EdgePluginInstallResult.Failed(compatibilityIssue!);
            }

            var result = await _packageInstaller
                .InstallAsync(
                    target,
                    release,
                    context.CloudOptions!,
                    context.HostVersion,
                    context.HostApiVersion,
                    CreateStepProgress(progress, stepBase, ordered.Count),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return result;
            }

            installedModuleIds.Add(release.ModuleId);
            stepBase += 100 / Math.Max(ordered.Count, 1);
        }

        _profileModuleConfigurationStore.EnableModules(target, installedModuleIds);
        progress?.Report(100);
        await ReportCurrentVersionsAsync(target, cancellationToken).ConfigureAwait(false);
        return EdgePluginInstallResult.Succeeded(installedModuleIds);
    }

    private static EdgeVersionStatus ResolveHostVersionStatus(string currentVersion, EdgeHostVersionEntry release)
    {
        if (string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase))
        {
            return EdgeVersionStatus.Deprecated;
        }

        var compare = CompareVersions(currentVersion, release.Version);
        return compare == 0
            ? EdgeVersionStatus.Current
            : compare < 0
                ? EdgeVersionStatus.Newer
                : EdgeVersionStatus.Older;
    }

    private static EdgeVersionStatus ResolvePluginVersionStatus(
        EdgePluginVersionRelease release,
        EdgeInstalledPlugin? installed,
        string hostVersion,
        string hostApiVersion,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy,
        out string? issue)
    {
        if (string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase))
        {
            issue = null;
            return EdgeVersionStatus.Deprecated;
        }

        if (!compatibilityPolicy.IsReleaseCompatible(release, hostVersion, hostApiVersion, out issue))
        {
            return EdgeVersionStatus.Incompatible;
        }

        if (installed is null)
        {
            return EdgeVersionStatus.NotInstalled;
        }

        var compare = CompareVersions(installed.Version, release.PackageVersion);
        return compare == 0
            ? EdgeVersionStatus.Current
            : compare < 0
                ? EdgeVersionStatus.Newer
                : EdgeVersionStatus.Older;
    }

    private async Task<OperationContext> CreateOperationContextAsync(
        EdgeUpdateTarget target,
        CancellationToken cancellationToken)
    {
        var releaseOptions = _configurationProvider.ResolveReleaseOptions();
        var hostVersion = ResolveHostVersion(target);
        var hostApiVersion = EdgeClientHostRuntime.HostApiVersion;
        var configuration = _configurationProvider.Resolve(target);
        if (!configuration.Success || configuration.Options is null)
        {
            return OperationContext.Failed(hostVersion, hostApiVersion, configuration.ErrorMessage ?? "CloudApi 配置不可用。");
        }

        var session = await _deviceSessionClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return OperationContext.Failed(hostVersion, hostApiVersion, session.ErrorMessage ?? "Cloud bootstrap 失败。");
        }

        var catalog = await LoadCatalogAsync(
            configuration.Options,
            session.Value,
            releaseOptions,
            cancellationToken).ConfigureAwait(false);
        if (!catalog.Success || catalog.Value is null)
        {
            return OperationContext.Failed(hostVersion, hostApiVersion, catalog.ErrorMessage ?? "Cloud catalog 不可用。");
        }

        return new OperationContext(configuration.Options, catalog.Value, hostVersion, hostApiVersion, null);
    }

    private async Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> LoadCatalogAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeReleaseOptions releaseOptions,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalogClient
            .GetCatalogAsync(options, session, releaseOptions, cancellationToken)
            .ConfigureAwait(false);
        if (catalog.Success && catalog.Value?.CatalogSchemaVersion != 2)
        {
            return EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(
                $"Cloud catalog schema 不匹配: {catalog.Value?.CatalogSchemaVersion}");
        }

        return catalog;
    }

    private static EdgeReleaseCatalogResult CreateResult(
        EdgeReleaseCatalogState state,
        EdgeReleaseOptions releaseOptions,
        string hostVersion,
        string hostApiVersion,
        IReadOnlyList<EdgeComponentVersionPlan> components,
        string? errorMessage = null)
        => new(
            state,
            releaseOptions.Channel,
            releaseOptions.TargetRuntime,
            hostVersion,
            hostApiVersion,
            components,
            errorMessage);

    private static IReadOnlyDictionary<string, IReadOnlyList<EdgePluginVersionRelease>> FlattenPluginVersions(
        EdgeReleaseCatalog catalog)
        => catalog.Plugins.ToDictionary(
            component => component.ModuleId,
            component => (IReadOnlyList<EdgePluginVersionRelease>)component.Versions
                .Select(version => new EdgePluginVersionRelease(
                    component.ModuleId,
                    string.IsNullOrWhiteSpace(component.DisplayName) ? component.ModuleId : component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    version))
                .OrderByDescending(release => release.PackageVersion, VersionStringComparer.Instance)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<EdgePluginVersionRelease> ResolveInstallOrder(
        EdgePluginVersionRelease target,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> releases,
        out string? issue)
    {
        var ordered = new List<EdgePluginVersionRelease>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resolvedIssue = null;

        bool Visit(EdgePluginVersionRelease release)
        {
            if (!visiting.Add(release.ModuleId))
            {
                resolvedIssue = $"插件依赖存在循环: {release.ModuleId}";
                return false;
            }

            foreach (var dependency in release.Dependencies ?? [])
            {
                if (string.IsNullOrWhiteSpace(dependency))
                {
                    continue;
                }

                if (!releases.TryGetValue(dependency.Trim(), out var dependencyRelease))
                {
                    resolvedIssue = $"插件 {release.ModuleId} 依赖 {dependency}，但 catalog 中没有该插件。";
                    return false;
                }

                if (!visited.Contains(dependencyRelease.ModuleId) && !Visit(dependencyRelease))
                {
                    return false;
                }
            }

            visiting.Remove(release.ModuleId);
            if (visited.Add(release.ModuleId))
            {
                ordered.Add(release);
            }

            resolvedIssue = null;
            return true;
        }

        var success = Visit(target);
        issue = resolvedIssue;
        return success ? ordered : [];
    }

    private static IProgress<int>? CreateStepProgress(IProgress<int>? progress, int stepBase, int stepCount)
        => progress is null
            ? null
            : new Progress<int>(value =>
            {
                var scaled = stepBase + value / Math.Max(stepCount, 1);
                progress.Report(Math.Clamp(scaled, 0, 99));
            });

    private static string ResolveHostVersion(EdgeUpdateTarget target)
    {
        var candidates = new[]
        {
            Path.Combine(target.HostDirectory, "IIoT.Edge.Host.Bootstrap.dll"),
            Path.Combine(target.HostDirectory, "IIoT.Edge.Shell.dll"),
            target.HostExecutablePath
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                {
                    continue;
                }

                var assemblyName = AssemblyName.GetAssemblyName(candidate);
                return EdgeClientHostRuntime.FormatHostVersion(assemblyName.Version);
            }
            catch (BadImageFormatException)
            {
            }
            catch (FileLoadException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return EdgeClientHostRuntime.FormatHostVersion(Assembly.GetEntryAssembly()?.GetName().Version);
    }

    private static int CompareVersions(string? left, string? right)
    {
        if (EdgeClientHostRuntime.TryParseVersion(left, out var leftVersion)
            && EdgeClientHostRuntime.TryParseVersion(right, out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return CompareVersions(x, y);
        }
    }

    private sealed record OperationContext(
        EdgeUpdateCloudApiOptions? CloudOptions,
        EdgeReleaseCatalog? Catalog,
        string HostVersion,
        string HostApiVersion,
        string? Error)
    {
        public static OperationContext Failed(string hostVersion, string hostApiVersion, string error)
            => new(null, null, hostVersion, hostApiVersion, error);
    }
}
