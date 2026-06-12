using System.Reflection;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherClientReleaseService
{
    Task<LauncherClientReleaseCheckResult> CheckAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default);

    Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
        LauncherProfileDefinition profile,
        string moduleId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default);
}

public sealed class LauncherClientReleaseService : ILauncherClientReleaseService
{
    private readonly ILauncherCloudApiConfigurationResolver _configurationResolver;
    private readonly ILauncherEdgeReleaseCloudClient _cloudClient;
    private readonly ILauncherInstalledPluginCatalog _installedPluginCatalog;
    private readonly ILauncherProfileModuleConfiguration _profileModuleConfiguration;
    private readonly ILauncherPluginPackageInstaller _packageInstaller;

    public LauncherClientReleaseService(
        ILauncherCloudApiConfigurationResolver configurationResolver,
        ILauncherEdgeReleaseCloudClient cloudClient,
        ILauncherInstalledPluginCatalog installedPluginCatalog,
        ILauncherProfileModuleConfiguration profileModuleConfiguration,
        ILauncherPluginPackageInstaller packageInstaller)
    {
        _configurationResolver = configurationResolver ?? throw new ArgumentNullException(nameof(configurationResolver));
        _cloudClient = cloudClient ?? throw new ArgumentNullException(nameof(cloudClient));
        _installedPluginCatalog = installedPluginCatalog ?? throw new ArgumentNullException(nameof(installedPluginCatalog));
        _profileModuleConfiguration = profileModuleConfiguration ?? throw new ArgumentNullException(nameof(profileModuleConfiguration));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
    }

    public async Task<LauncherClientReleaseCheckResult> CheckAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var releaseOptions = _configurationResolver.ResolveReleaseOptions();
        var hostVersion = ResolveHostVersion(profile);
        var hostApiVersion = EdgeClientHostRuntime.HostApiVersion;
        var configuration = _configurationResolver.Resolve(profile);
        if (!configuration.Success || configuration.Options is null)
        {
            return new LauncherClientReleaseCheckResult(
                LauncherClientReleaseCheckState.NotConfigured,
                releaseOptions.Channel,
                releaseOptions.TargetRuntime,
                hostVersion,
                hostApiVersion,
                null,
                [],
                configuration.ErrorMessage);
        }

        var session = await _cloudClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return new LauncherClientReleaseCheckResult(
                LauncherClientReleaseCheckState.BootstrapFailed,
                releaseOptions.Channel,
                releaseOptions.TargetRuntime,
                hostVersion,
                hostApiVersion,
                null,
                [],
                session.ErrorMessage);
        }

        var catalog = await _cloudClient
            .GetCatalogAsync(configuration.Options, session.Value, releaseOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!catalog.Success || catalog.Value is null)
        {
            return new LauncherClientReleaseCheckResult(
                LauncherClientReleaseCheckState.CatalogUnavailable,
                releaseOptions.Channel,
                releaseOptions.TargetRuntime,
                hostVersion,
                hostApiVersion,
                null,
                [],
                catalog.ErrorMessage);
        }

        var installedPlugins = _installedPluginCatalog.LoadInstalledPlugins(profile);
        var enabledPlugins = _profileModuleConfiguration.ReadEnabledModules(profile);
        _ = await _cloudClient
            .ReportVersionAsync(
                configuration.Options,
                session.Value,
                releaseOptions,
                profile.MachineProfile,
                hostVersion,
                hostApiVersion,
                installedPlugins,
                enabledPlugins,
                cancellationToken)
            .ConfigureAwait(false);

        return new LauncherClientReleaseCheckResult(
            LauncherClientReleaseCheckState.Succeeded,
            releaseOptions.Channel,
            releaseOptions.TargetRuntime,
            hostVersion,
            hostApiVersion,
            catalog.Value.LatestHost?.Version,
            BuildPluginPlans(catalog.Value.PluginReleases, installedPlugins, hostVersion, hostApiVersion));
    }

    public async Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
        LauncherProfileDefinition profile,
        string moduleId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var releaseOptions = _configurationResolver.ResolveReleaseOptions();
        var hostVersion = ResolveHostVersion(profile);
        var hostApiVersion = EdgeClientHostRuntime.HostApiVersion;
        var configuration = _configurationResolver.Resolve(profile);
        if (!configuration.Success || configuration.Options is null)
        {
            return LauncherPluginInstallResult.Failed(configuration.ErrorMessage ?? "CloudApi 配置不可用。");
        }

        var session = await _cloudClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return LauncherPluginInstallResult.Failed(session.ErrorMessage ?? "Cloud bootstrap 失败。");
        }

        var catalog = await _cloudClient
            .GetCatalogAsync(configuration.Options, session.Value, releaseOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!catalog.Success || catalog.Value is null)
        {
            return LauncherPluginInstallResult.Failed(catalog.ErrorMessage ?? "Cloud catalog 不可用。");
        }

        var releases = catalog.Value.PluginReleases
            .GroupBy(static release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(static release => release.Version, VersionStringComparer.Instance).First(),
                StringComparer.OrdinalIgnoreCase);
        if (!releases.TryGetValue(moduleId.Trim(), out var target))
        {
            return LauncherPluginInstallResult.Failed($"Cloud catalog 中未找到插件: {moduleId}");
        }

        var ordered = ResolveInstallOrder(target, releases, out var dependencyIssue);
        if (dependencyIssue is not null)
        {
            return LauncherPluginInstallResult.Failed(dependencyIssue);
        }

        var installedModuleIds = new List<string>();
        var stepBase = 0;
        foreach (var release in ordered)
        {
            var stepProgress = new Progress<int>(value =>
            {
                var scaled = stepBase + value / Math.Max(ordered.Count, 1);
                progress?.Report(Math.Clamp(scaled, 0, 99));
            });
            var result = await _packageInstaller
                .InstallAsync(
                    profile,
                    release,
                    configuration.Options,
                    hostVersion,
                    hostApiVersion,
                    stepProgress,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return result;
            }

            installedModuleIds.Add(release.ModuleId);
            stepBase += 100 / Math.Max(ordered.Count, 1);
        }

        _profileModuleConfiguration.EnableModules(profile, installedModuleIds);
        progress?.Report(100);
        await ReportCurrentVersionsAsync(profile, cancellationToken).ConfigureAwait(false);

        return LauncherPluginInstallResult.Succeeded(installedModuleIds);
    }

    public async Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var configuration = _configurationResolver.Resolve(profile);
        if (!configuration.Success || configuration.Options is null)
        {
            return LauncherVersionReportResult.Failed(configuration.ErrorMessage ?? "CloudApi 配置不可用。");
        }

        var session = await _cloudClient
            .BootstrapAsync(configuration.Options, cancellationToken)
            .ConfigureAwait(false);
        if (!session.Success || session.Value is null)
        {
            return LauncherVersionReportResult.Failed(session.ErrorMessage ?? "Cloud bootstrap 失败。");
        }

        return await _cloudClient
            .ReportVersionAsync(
                configuration.Options,
                session.Value,
                _configurationResolver.ResolveReleaseOptions(),
                profile.MachineProfile,
                ResolveHostVersion(profile),
                EdgeClientHostRuntime.HostApiVersion,
                _installedPluginCatalog.LoadInstalledPlugins(profile),
                _profileModuleConfiguration.ReadEnabledModules(profile),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static IReadOnlyList<LauncherPluginUpdatePlan> BuildPluginPlans(
        IReadOnlyList<LauncherClientPluginRelease> releases,
        IReadOnlyList<LauncherInstalledPlugin> installedPlugins,
        string hostVersion,
        string hostApiVersion)
    {
        var installedByModule = installedPlugins.ToDictionary(
            static plugin => plugin.ModuleId,
            StringComparer.OrdinalIgnoreCase);

        return releases
            .GroupBy(static release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderByDescending(static release => release.Version, VersionStringComparer.Instance).First())
            .OrderBy(static release => release.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(release =>
            {
                installedByModule.TryGetValue(release.ModuleId, out var installed);
                if (!LauncherPluginPackageInstaller.IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
                {
                    return new LauncherPluginUpdatePlan(
                        release,
                        installed,
                        LauncherPluginUpdateState.Incompatible,
                        issue);
                }

                var state = installed is null
                    ? LauncherPluginUpdateState.NotInstalled
                    : CompareVersions(installed.Version, release.Version) < 0
                        ? LauncherPluginUpdateState.UpdateAvailable
                        : CompareVersions(installed.Version, release.Version) > 0
                            ? LauncherPluginUpdateState.InstalledNewer
                            : LauncherPluginUpdateState.Latest;
                return new LauncherPluginUpdatePlan(release, installed, state, null);
            })
            .ToArray();
    }

    private static IReadOnlyList<LauncherClientPluginRelease> ResolveInstallOrder(
        LauncherClientPluginRelease target,
        IReadOnlyDictionary<string, LauncherClientPluginRelease> releases,
        out string? issue)
    {
        var ordered = new List<LauncherClientPluginRelease>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? resolvedIssue = null;

        bool Visit(LauncherClientPluginRelease release)
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

    private static string ResolveHostVersion(LauncherProfileDefinition profile)
    {
        var hostDirectory = LauncherCloudApiConfigurationResolver.ResolveHostDirectory(profile);
        var candidates = new[]
        {
            Path.Combine(hostDirectory, "IIoT.Edge.Host.Bootstrap.dll"),
            Path.Combine(hostDirectory, "IIoT.Edge.Shell.dll"),
            profile.ExecutablePath
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

        return EdgeClientHostRuntime.ResolveHostVersion(Assembly.GetExecutingAssembly());
    }

    private static int CompareVersions(string? left, string? right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static readonly VersionStringComparer Instance = new();

        public int Compare(string? x, string? y)
            => CompareVersions(x, y);
    }
}

public sealed class NullLauncherClientReleaseService : ILauncherClientReleaseService
{
    public static readonly NullLauncherClientReleaseService Instance = new();

    private NullLauncherClientReleaseService()
    {
    }

    public Task<LauncherClientReleaseCheckResult> CheckAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new LauncherClientReleaseCheckResult(
            LauncherClientReleaseCheckState.NotConfigured,
            "stable",
            "win-x64",
            "0.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            null,
            [],
            "客户端插件更新服务未注册。"));

    public Task<LauncherPluginInstallResult> InstallOrUpdatePluginAsync(
        LauncherProfileDefinition profile,
        string moduleId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(LauncherPluginInstallResult.Failed("客户端插件更新服务未注册。"));

    public Task<LauncherVersionReportResult> ReportCurrentVersionsAsync(
        LauncherProfileDefinition profile,
        CancellationToken cancellationToken = default)
        => Task.FromResult(LauncherVersionReportResult.Failed("客户端插件更新服务未注册。"));
}
