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
    private readonly IEdgeVersionCompatibilityPolicy _compatibilityPolicy;
    private readonly IEdgeReleaseSourceValidator? _releaseSourceValidator;
    private readonly IEdgePluginCompositionTransaction? _compositionTransaction;

    public EdgeReleaseService(
        IEdgeUpdateConfigurationProvider configurationProvider,
        IEdgeUpdateDeviceSessionClient deviceSessionClient,
        IEdgeUpdateCatalogClient catalogClient,
        IEdgeVersionReporter versionReporter,
        IEdgeInstalledPluginCatalog installedPluginCatalog,
        IEdgeProfileModuleConfigurationStore profileModuleConfigurationStore,
        IEdgePluginPackageInstaller packageInstaller,
        IEdgeHostUpdateService hostUpdateService,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy,
        IEdgeReleaseSourceValidator? releaseSourceValidator = null,
        IEdgePluginCompositionTransaction? compositionTransaction = null)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _deviceSessionClient = deviceSessionClient ?? throw new ArgumentNullException(nameof(deviceSessionClient));
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _versionReporter = versionReporter ?? throw new ArgumentNullException(nameof(versionReporter));
        _installedPluginCatalog = installedPluginCatalog ?? throw new ArgumentNullException(nameof(installedPluginCatalog));
        _profileModuleConfigurationStore = profileModuleConfigurationStore ?? throw new ArgumentNullException(nameof(profileModuleConfigurationStore));
        _packageInstaller = packageInstaller ?? throw new ArgumentNullException(nameof(packageInstaller));
        _hostUpdateService = hostUpdateService ?? throw new ArgumentNullException(nameof(hostUpdateService));
        _compatibilityPolicy = compatibilityPolicy ?? throw new ArgumentNullException(nameof(compatibilityPolicy));
        _releaseSourceValidator = releaseSourceValidator;
        _compositionTransaction = compositionTransaction;
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
        var releaseSourceIssue = _releaseSourceValidator?.ValidateConfiguredSource();
        if (releaseSourceIssue is not null)
        {
            return CreateResult(
                EdgeReleaseCatalogState.CatalogUnavailable,
                releaseOptions,
                hostVersion,
                hostApiVersion,
                BuildLocalVersionPlans(installedPlugins, hostVersion),
                releaseSourceIssue);
        }

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
            context.HostVersion,
            context.HostApiVersion,
            reportAfterInstall: true,
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
        if (release is null)
        {
            return new EdgeHostUpdateApplyResult(false, $"Cloud catalog 中未找到宿主版本 {version}。");
        }

        var option = BuildHostVersionOption(
            release,
            context.HostVersion,
            context.Catalog,
            _installedPluginCatalog.LoadInstalledPlugins(target),
            CreateEnabledModuleSet(_profileModuleConfigurationStore.ReadEnabledModules(target)),
            _compatibilityPolicy);
        if (!option.CanApply)
        {
            return new EdgeHostUpdateApplyResult(
                false,
                option.CompatibilityIssue ?? $"宿主版本 {version} 当前不可应用。");
        }

        if (option.RequiredComposition?.PluginVersions.Count > 0)
        {
            return new EdgeHostUpdateApplyResult(
                false,
                option.CompatibilityIssue ?? $"宿主版本 {version} 必须通过完整插件组合应用。");
        }

        return await ApplyHostReleaseAsync(release, progress, cancellationToken).ConfigureAwait(false);
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
        EdgeHostVersionEntry? targetHostRelease = null;
        var compatibilityHostVersion = context.HostVersion;
        var compatibilityHostApiVersion = context.HostApiVersion;
        if (!string.IsNullOrWhiteSpace(selection.HostVersion))
        {
            targetHostRelease = context.Catalog!.Host.Versions.FirstOrDefault(entry =>
                string.Equals(entry.Version, selection.HostVersion.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetHostRelease is null)
            {
                return EdgePluginInstallResult.Failed(
                    $"Cloud catalog 中未找到宿主版本 {selection.HostVersion}。");
            }

            var hostOption = BuildHostVersionOption(
                targetHostRelease,
                context.HostVersion,
                context.Catalog,
                _installedPluginCatalog.LoadInstalledPlugins(target),
                CreateEnabledModuleSet(_profileModuleConfigurationStore.ReadEnabledModules(target)),
                _compatibilityPolicy);
            if (!hostOption.CanApply)
            {
                return EdgePluginInstallResult.Failed(
                    hostOption.CompatibilityIssue ?? $"宿主版本 {selection.HostVersion} 当前不可应用。");
            }

            foreach (var required in hostOption.RequiredComposition?.PluginVersions
                         ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                if (!selection.PluginVersions.TryGetValue(required.Key, out var selectedVersion)
                    || !string.Equals(selectedVersion, required.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return EdgePluginInstallResult.Failed(
                        $"宿主 {selection.HostVersion} 的插件组合不完整：必须准备 {required.Key} {required.Value}。");
                }
            }

            compatibilityHostVersion = targetHostRelease.Version;
            compatibilityHostApiVersion = targetHostRelease.HostApiVersion;
        }

        var selectedByModule = releases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.FirstOrDefault(release =>
                        !string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)
                        && _compatibilityPolicy.IsReleaseCompatible(
                            release,
                            compatibilityHostVersion,
                            compatibilityHostApiVersion,
                            out _))
                    ?? pair.Value.First(),
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

        var compositionIssue = ValidateCompositionBeforeInstall(
            target,
            context.Catalog!,
            selectedByModule,
            selection,
            compatibilityHostVersion,
            compatibilityHostApiVersion);
        if (compositionIssue is not null)
        {
            return EdgePluginInstallResult.Failed(compositionIssue);
        }

        var orderedReleases = ResolveCompositionInstallOrder(
            selection.PluginVersions.Keys,
            selectedByModule,
            out var installOrderIssue);
        if (installOrderIssue is not null)
        {
            return EdgePluginInstallResult.Failed(installOrderIssue);
        }

        var pendingHostVersion = targetHostRelease?.Version;
        var installResult = await InstallCompositionReleasesAsync(
            [new EdgePluginCompositionTarget(
                target,
                orderedReleases
                    .Select(static release => release.ModuleId)
                    .ToArray())],
            BindReleaseSources(orderedReleases, context.CloudOptions!),
            compatibilityHostVersion,
            compatibilityHostApiVersion,
            pendingHostVersion,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!installResult.Success)
        {
            return installResult;
        }

        if (targetHostRelease is not null)
        {
            var hostResult = await ApplyHostReleaseWithRollbackAsync(
                targetHostRelease,
                orderedReleases.Count > 0,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!hostResult.Success)
            {
                return hostResult;
            }
        }

        progress?.Report(100);
        return installResult;
    }

    public async Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
        IReadOnlyList<EdgeUpdateTarget> targets,
        EdgeVersionSelection selection,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(selection);
        if (targets.Count == 0)
        {
            return EdgePluginInstallResult.Failed("没有可用于组合升级的工序目标。");
        }

        if (targets.Count == 1)
        {
            return await ApplyVersionCompositionAsync(
                targets[0],
                selection,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(selection.HostVersion))
        {
            return EdgePluginInstallResult.Failed("跨多个工序应用插件时必须指定同一目标宿主版本。");
        }

        if (!TargetsShareHostLayout(targets, out var layoutIssue))
        {
            return EdgePluginInstallResult.Failed(layoutIssue!);
        }

        var targetContexts = new List<CompositionTargetContext>();
        foreach (var target in targets)
        {
            var context = await CreateOperationContextAsync(target, cancellationToken).ConfigureAwait(false);
            if (context.Error is not null)
            {
                return EdgePluginInstallResult.Failed(
                    $"工序 {target.MachineProfile} 无法取得真实发布目录：{context.Error}");
            }

            var targetHostRelease = context.Catalog!.Host.Versions.FirstOrDefault(entry =>
                string.Equals(entry.Version, selection.HostVersion.Trim(), StringComparison.OrdinalIgnoreCase));
            if (targetHostRelease is null)
            {
                return EdgePluginInstallResult.Failed(
                    $"工序 {target.MachineProfile} 的 Cloud catalog 中没有宿主版本 {selection.HostVersion}。");
            }

            var enabledModules = new HashSet<string>(
                _profileModuleConfigurationStore.ReadEnabledModules(target),
                StringComparer.OrdinalIgnoreCase);
            var hostOption = BuildHostVersionOption(
                targetHostRelease,
                context.HostVersion,
                context.Catalog,
                _installedPluginCatalog.LoadInstalledPlugins(target),
                enabledModules,
                _compatibilityPolicy);
            if (!hostOption.CanApply)
            {
                return EdgePluginInstallResult.Failed(
                    $"工序 {target.MachineProfile} 无法应用宿主 {selection.HostVersion}：{hostOption.CompatibilityIssue ?? "组合不兼容。"}");
            }

            foreach (var required in hostOption.RequiredComposition?.PluginVersions
                         ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            {
                if (!selection.PluginVersions.TryGetValue(required.Key, out var selectedVersion)
                    || !string.Equals(selectedVersion, required.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return EdgePluginInstallResult.Failed(
                        $"工序 {target.MachineProfile} 的宿主 {selection.HostVersion} 组合不完整：必须准备 {required.Key} {required.Value}。");
                }
            }

            targetContexts.Add(new CompositionTargetContext(
                target,
                context,
                targetHostRelease,
                enabledModules));
        }

        var canonicalHostRelease = targetContexts[0].HostRelease;
        foreach (var targetContext in targetContexts.Skip(1))
        {
            if (!HasSameHostArtifact(canonicalHostRelease, targetContext.HostRelease))
            {
                return EdgePluginInstallResult.Failed(
                    $"工序 {targetContext.Target.MachineProfile} 的宿主 {selection.HostVersion} artifact 与其他工序不一致。");
            }
        }

        var selectedByModule = MergePluginVersions(
            targetContexts.Select(static item => item.Operation.Catalog!),
            canonicalHostRelease.Version,
            canonicalHostRelease.HostApiVersion,
            out var mergeIssue);
        if (mergeIssue is not null)
        {
            return EdgePluginInstallResult.Failed(mergeIssue);
        }

        var requestedReleaseSources = new Dictionary<string, EdgePluginCompositionRelease>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in selection.PluginVersions)
        {
            var enabledContexts = targetContexts
                .Where(context => context.EnabledModules.Contains(item.Key))
                .ToArray();
            if (enabledContexts.Length == 0)
            {
                return EdgePluginInstallResult.Failed(
                    $"插件 {item.Key} 未在任何本次工序 profile 中启用，拒绝把它加入宿主组合。");
            }

            CompositionTargetContext? owner = null;
            EdgePluginVersionRelease? selected = null;
            foreach (var candidateOwner in enabledContexts)
            {
                selected = FindCatalogRelease(
                    candidateOwner.Operation.Catalog!,
                    item.Key,
                    item.Value);
                if (selected is not null)
                {
                    owner = candidateOwner;
                    break;
                }
            }

            if (owner is null || selected is null)
            {
                return EdgePluginInstallResult.Failed(
                    $"启用插件 {item.Key} 的工序 Cloud catalog 中没有版本 {item.Value}，拒绝跨用其他工序的下载源。");
            }

            selectedByModule[item.Key] = [selected];
            requestedReleaseSources[item.Key] = new EdgePluginCompositionRelease(
                selected,
                owner.Operation.CloudOptions!);
        }

        var selectedReleaseByModule = selectedByModule.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value.FirstOrDefault(release =>
                        !string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)
                        && _compatibilityPolicy.IsReleaseCompatible(
                            release,
                            canonicalHostRelease.Version,
                            canonicalHostRelease.HostApiVersion,
                            out _))
                    ?? pair.Value.First(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in selection.PluginVersions)
        {
            selectedReleaseByModule[item.Key] = selectedByModule[item.Key][0];
        }

        foreach (var targetContext in targetContexts)
        {
            var compositionIssue = ValidateCompositionBeforeInstall(
                targetContext.Target,
                targetContext.Operation.Catalog!,
                selectedReleaseByModule,
                selection,
                canonicalHostRelease.Version,
                canonicalHostRelease.HostApiVersion);
            if (compositionIssue is not null)
            {
                return EdgePluginInstallResult.Failed(
                    $"工序 {targetContext.Target.MachineProfile} 的组合校验失败：{compositionIssue}");
            }
        }

        var orderedReleases = ResolveCompositionInstallOrder(
            selection.PluginVersions.Keys,
            selectedReleaseByModule,
            out var installOrderIssue);
        if (installOrderIssue is not null)
        {
            return EdgePluginInstallResult.Failed(installOrderIssue);
        }

        var ownedReleases = new List<EdgePluginCompositionRelease>(orderedReleases.Count);
        foreach (var release in orderedReleases)
        {
            if (requestedReleaseSources.TryGetValue(release.ModuleId, out var requested))
            {
                ownedReleases.Add(requested);
                continue;
            }

            EdgePluginCompositionRelease? ownedDependency = null;
            foreach (var targetContext in targetContexts)
            {
                var advertised = FindCatalogRelease(
                    targetContext.Operation.Catalog!,
                    release);
                if (advertised is null)
                {
                    continue;
                }

                ownedDependency = new EdgePluginCompositionRelease(
                    advertised,
                    targetContext.Operation.CloudOptions!);
                break;
            }

            if (ownedDependency is null)
            {
                return EdgePluginInstallResult.Failed(
                    $"插件 {release.ModuleId} {release.PackageVersion} 没有可追溯的工序 catalog 下载源。");
            }

            ownedReleases.Add(ownedDependency);
        }

        var transactionTargets = targetContexts
            .Select(targetContext => new EdgePluginCompositionTarget(
                targetContext.Target,
                ResolveTargetTransactionModules(
                    targetContext.EnabledModules,
                    selection.PluginVersions.Keys,
                    selectedReleaseByModule)))
            .ToArray();
        var installResult = await InstallCompositionReleasesAsync(
            transactionTargets,
            ownedReleases,
            canonicalHostRelease.Version,
            canonicalHostRelease.HostApiVersion,
            canonicalHostRelease.Version,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!installResult.Success)
        {
            return installResult;
        }

        var hostResult = await ApplyHostReleaseWithRollbackAsync(
            canonicalHostRelease,
            orderedReleases.Count > 0,
            progress,
            cancellationToken).ConfigureAwait(false);
        if (!hostResult.Success)
        {
            return hostResult;
        }

        progress?.Report(100);
        return installResult;
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

        var installedByModule = installedPlugins.ToDictionary(
            static plugin => plugin.ModuleId,
            StringComparer.OrdinalIgnoreCase);
        var plannedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledModuleSet = CreateEnabledModuleSet(enabledModuleIds);
        var plans = new List<EdgeComponentVersionPlan>
        {
            new(
                EdgeComponentKind.Host,
                "Host",
                string.IsNullOrWhiteSpace(catalog.Host.DisplayName) ? "Edge Host" : catalog.Host.DisplayName,
                hostVersion,
                catalog.Host.Versions
                    .Select(version => BuildHostVersionOption(
                        version,
                        hostVersion,
                        catalog,
                        installedPlugins,
                        enabledModuleSet,
                        compatibilityPolicy))
                    .ToList())
        };
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

    private static bool TargetsShareHostLayout(
        IReadOnlyList<EdgeUpdateTarget> targets,
        out string? issue)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        try
        {
            var canonicalDirectory = Path.GetFullPath(targets[0].HostDirectory);
            var canonicalExecutable = Path.GetFullPath(targets[0].HostExecutablePath);
            foreach (var target in targets.Skip(1))
            {
                if (!string.Equals(canonicalDirectory, Path.GetFullPath(target.HostDirectory), comparison)
                    || !string.Equals(canonicalExecutable, Path.GetFullPath(target.HostExecutablePath), comparison))
                {
                    issue = "多个工序不属于同一 Host 布局，不能合并为一次宿主更新。";
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issue = $"组合升级的 Host 路径无效：{ex.Message}";
            return false;
        }

        issue = null;
        return true;
    }

    private static bool HasSameHostArtifact(
        EdgeHostVersionEntry left,
        EdgeHostVersionEntry right)
        => string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.HostApiVersion, right.HostApiVersion, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.TargetRuntime, right.TargetRuntime, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.TargetFramework, right.TargetFramework, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
           && left.PackageSize == right.PackageSize;

    private static Dictionary<string, List<EdgePluginVersionRelease>> MergePluginVersions(
        IEnumerable<EdgeReleaseCatalog> catalogs,
        string targetHostVersion,
        string targetHostApiVersion,
        out string? issue)
    {
        var merged = new Dictionary<string, List<EdgePluginVersionRelease>>(StringComparer.OrdinalIgnoreCase);
        foreach (var catalog in catalogs)
        {
            foreach (var pair in FlattenPluginVersions(catalog))
            {
                if (!merged.TryGetValue(pair.Key, out var releases))
                {
                    releases = [];
                    merged[pair.Key] = releases;
                }

                foreach (var release in pair.Value)
                {
                    var existing = releases.FirstOrDefault(candidate =>
                        string.Equals(candidate.PackageVersion, release.PackageVersion, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        releases.Add(release);
                        continue;
                    }

                    if (!HasSamePluginArtifact(existing, release))
                    {
                        issue = $"不同工序 catalog 对插件 {release.ModuleId} {release.PackageVersion} 给出了不一致的 artifact。";
                        return merged;
                    }
                }
            }
        }

        foreach (var pair in merged)
        {
            pair.Value.Sort((left, right) =>
            {
                var leftCompatible = IsCompatibleWithTarget(left, targetHostVersion, targetHostApiVersion);
                var rightCompatible = IsCompatibleWithTarget(right, targetHostVersion, targetHostApiVersion);
                if (leftCompatible != rightCompatible)
                {
                    return leftCompatible ? -1 : 1;
                }

                return VersionStringComparer.Instance.Compare(right.PackageVersion, left.PackageVersion);
            });
        }

        issue = null;
        return merged;
    }

    private static bool IsCompatibleWithTarget(
        EdgePluginVersionRelease release,
        string hostVersion,
        string hostApiVersion)
        => string.Equals(release.HostApiVersion, hostApiVersion, StringComparison.OrdinalIgnoreCase)
           && CompareVersions(hostVersion, release.MinHostVersion) >= 0
           && CompareVersions(hostVersion, release.MaxHostVersion) <= 0
           && !string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase);

    private static bool HasSamePluginArtifact(
        EdgePluginVersionRelease left,
        EdgePluginVersionRelease right)
        => string.Equals(left.ModuleId, right.ModuleId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.PackageVersion, right.PackageVersion, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.HostApiVersion, right.HostApiVersion, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.MinHostVersion, right.MinHostVersion, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.MaxHostVersion, right.MaxHostVersion, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.TargetRuntime, right.TargetRuntime, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.TargetFramework, right.TargetFramework, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Sha256, right.Sha256, StringComparison.OrdinalIgnoreCase)
           && left.PackageSize == right.PackageSize
           && string.Equals(left.Status, right.Status, StringComparison.OrdinalIgnoreCase)
           && left.Dependencies.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
               .SequenceEqual(
                   right.Dependencies.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase),
                   StringComparer.OrdinalIgnoreCase);

    private static EdgePluginVersionRelease? FindCatalogRelease(
        EdgeReleaseCatalog catalog,
        string moduleId,
        string packageVersion)
        => FlattenPluginVersions(catalog).TryGetValue(moduleId, out var releases)
            ? releases.FirstOrDefault(release =>
                string.Equals(
                    release.PackageVersion,
                    packageVersion,
                    StringComparison.OrdinalIgnoreCase))
            : null;

    private static EdgePluginVersionRelease? FindCatalogRelease(
        EdgeReleaseCatalog catalog,
        EdgePluginVersionRelease expected)
        => FlattenPluginVersions(catalog).TryGetValue(expected.ModuleId, out var releases)
            ? releases.FirstOrDefault(release =>
                string.Equals(
                    release.PackageVersion,
                    expected.PackageVersion,
                    StringComparison.OrdinalIgnoreCase)
                && HasSamePluginArtifact(release, expected))
            : null;

    private static EdgeVersionOption BuildHostVersionOption(
        EdgeHostVersionEntry targetHost,
        string currentHostVersion,
        EdgeReleaseCatalog catalog,
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
        IReadOnlySet<string>? enabledModuleIds,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy)
    {
        var status = ResolveHostVersionStatus(currentHostVersion, targetHost);
        if (string.Equals(currentHostVersion, targetHost.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new EdgeVersionOption(
                targetHost.Version,
                status,
                false,
                null,
                HostRelease: new EdgeHostVersionRelease(targetHost));
        }

        var releases = FlattenPluginVersions(catalog);
        var requiredPlugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        var installedModuleIds = new HashSet<string>(
            installedPlugins.Select(static plugin => plugin.ModuleId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var installed in installedPlugins
                     .Where(plugin => IsModuleVisible(plugin.ModuleId, enabledModuleIds))
                     .OrderBy(static plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase))
        {
            if (!releases.TryGetValue(installed.ModuleId, out var moduleReleases))
            {
                issues.Add($"Cloud catalog 中没有已启用插件 {installed.ModuleId} 的发布记录。");
                continue;
            }

            var installedRelease = moduleReleases.FirstOrDefault(release =>
                string.Equals(release.PackageVersion, installed.Version, StringComparison.OrdinalIgnoreCase));
            if (installedRelease is not null
                && !string.Equals(installedRelease.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)
                && compatibilityPolicy.IsReleaseCompatible(
                    installedRelease,
                    targetHost.Version,
                    targetHost.HostApiVersion,
                    out _))
            {
                continue;
            }

            var replacement = moduleReleases.FirstOrDefault(release =>
                !string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)
                && compatibilityPolicy.IsReleaseCompatible(
                    release,
                    targetHost.Version,
                    targetHost.HostApiVersion,
                    out _));
            if (replacement is null)
            {
                var currentDescription = installedRelease is null
                    ? $"本机版本 {installed.Version} 未出现在当前 catalog"
                    : $"本机版本 {installed.Version} 与目标宿主不兼容";
                issues.Add(
                    $"插件 {installed.ModuleId} {currentDescription}，且 catalog 中没有兼容宿主 {targetHost.Version} / API {targetHost.HostApiVersion} 的可用版本。");
                continue;
            }

            requiredPlugins[installed.ModuleId] = replacement.PackageVersion;
        }

        if (enabledModuleIds is not null)
        {
            foreach (var enabledModuleId in enabledModuleIds
                         .Where(moduleId => !installedModuleIds.Contains(moduleId))
                         .OrderBy(static moduleId => moduleId, StringComparer.OrdinalIgnoreCase))
            {
                if (!releases.TryGetValue(enabledModuleId, out var moduleReleases))
                {
                    issues.Add($"Cloud catalog 中没有已启用但未安装的插件 {enabledModuleId} 发布记录。");
                    continue;
                }

                var replacement = moduleReleases.FirstOrDefault(release =>
                    !string.Equals(release.Status, "Deprecated", StringComparison.OrdinalIgnoreCase)
                    && compatibilityPolicy.IsReleaseCompatible(
                        release,
                        targetHost.Version,
                        targetHost.HostApiVersion,
                        out _));
                if (replacement is null)
                {
                    issues.Add(
                        $"已启用插件 {enabledModuleId} 尚未安装，且 catalog 中没有兼容宿主 {targetHost.Version} / API {targetHost.HostApiVersion} 的可用版本。");
                    continue;
                }

                requiredPlugins[enabledModuleId] = replacement.PackageVersion;
            }
        }

        if (issues.Count > 0)
        {
            return new EdgeVersionOption(
                targetHost.Version,
                status,
                false,
                string.Join(Environment.NewLine, issues),
                HostRelease: new EdgeHostVersionRelease(targetHost));
        }

        var issue = requiredPlugins.Count == 0
            ? null
            : $"目标宿主 {targetHost.Version} 需要先准备插件组合：{string.Join(", ", requiredPlugins.Select(static item => $"{item.Key} {item.Value}"))}。";
        return new EdgeVersionOption(
            targetHost.Version,
            status,
            true,
            issue,
            HostRelease: new EdgeHostVersionRelease(targetHost),
            RequiredComposition: new EdgeVersionSelection(targetHost.Version, requiredPlugins));
    }

    private string? ValidateCompositionBeforeInstall(
        EdgeUpdateTarget target,
        EdgeReleaseCatalog catalog,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> selectedByModule,
        EdgeVersionSelection selection,
        string targetHostVersion,
        string targetHostApiVersion)
    {
        foreach (var item in selection.PluginVersions)
        {
            var release = selectedByModule[item.Key];
            var ordered = ResolveInstallOrder(release, selectedByModule, out var dependencyIssue);
            if (dependencyIssue is not null)
            {
                return dependencyIssue;
            }

            foreach (var candidate in ordered)
            {
                if (!_compatibilityPolicy.IsReleaseCompatible(
                        candidate,
                        targetHostVersion,
                        targetHostApiVersion,
                        out var compatibilityIssue))
                {
                    return compatibilityIssue;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(selection.HostVersion))
        {
            return null;
        }

        var enabledModules = CreateEnabledModuleSet(_profileModuleConfigurationStore.ReadEnabledModules(target));
        var releases = FlattenPluginVersions(catalog);
        foreach (var installed in _installedPluginCatalog.LoadInstalledPlugins(target)
                     .Where(plugin => IsModuleVisible(plugin.ModuleId, enabledModules)))
        {
            if (selection.PluginVersions.ContainsKey(installed.ModuleId))
            {
                continue;
            }

            if (!releases.TryGetValue(installed.ModuleId, out var moduleReleases)
                || moduleReleases.FirstOrDefault(release =>
                    string.Equals(release.PackageVersion, installed.Version, StringComparison.OrdinalIgnoreCase)) is not { } installedRelease)
            {
                return $"宿主 {selection.HostVersion} 的插件组合不完整：catalog 中没有本机插件 {installed.ModuleId} {installed.Version}。";
            }

            if (!_compatibilityPolicy.IsReleaseCompatible(
                    installedRelease,
                    targetHostVersion,
                    targetHostApiVersion,
                    out var issue))
            {
                return issue;
            }
        }

        return null;
    }

    private Task<EdgeHostUpdateApplyResult> ApplyHostReleaseAsync(
        EdgeHostVersionEntry release,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
        => _hostUpdateService.ApplyVersionAsync(
            new EdgeHostVersionRelease(release),
            progress,
            cancellationToken);

    private async Task<EdgePluginInstallResult> InstallPluginReleasesAsync(
        EdgeUpdateTarget target,
        OperationContext context,
        EdgePluginVersionRelease targetRelease,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> selectedByModule,
        string compatibilityHostVersion,
        string compatibilityHostApiVersion,
        bool reportAfterInstall,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var ordered = ResolveInstallOrder(targetRelease, selectedByModule, out var dependencyIssue);
        if (dependencyIssue is not null)
        {
            return EdgePluginInstallResult.Failed(dependencyIssue);
        }

        foreach (var release in ordered)
        {
            if (!_compatibilityPolicy.IsReleaseCompatible(
                    release,
                    compatibilityHostVersion,
                    compatibilityHostApiVersion,
                    out var compatibilityIssue))
            {
                return EdgePluginInstallResult.Failed(compatibilityIssue!);
            }
        }

        EdgePluginInstallResult result;
        if (_compositionTransaction is not null)
        {
            result = await _compositionTransaction
                .InstallAsync(
                    [new EdgePluginCompositionTarget(
                        target,
                        ordered
                            .Select(static release => release.ModuleId)
                            .ToArray())],
                    BindReleaseSources(ordered, context.CloudOptions!),
                    compatibilityHostVersion,
                    compatibilityHostApiVersion,
                    pendingHostVersion: null,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var installedModuleIds = new List<string>();
            var stepBase = 0;
            foreach (var release in ordered)
            {
                result = await _packageInstaller
                .InstallAsync(
                    target,
                    release,
                    context.CloudOptions!,
                    compatibilityHostVersion,
                    compatibilityHostApiVersion,
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
            result = EdgePluginInstallResult.Succeeded(installedModuleIds);
        }

        if (!result.Success)
        {
            return result;
        }

        progress?.Report(100);
        if (reportAfterInstall)
        {
            await ReportCurrentVersionsAsync(target, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private async Task<EdgePluginInstallResult> InstallCompositionReleasesAsync(
        IReadOnlyList<EdgePluginCompositionTarget> targets,
        IReadOnlyList<EdgePluginCompositionRelease> releases,
        string compatibilityHostVersion,
        string compatibilityHostApiVersion,
        string? pendingHostVersion,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (releases.Count == 0)
        {
            return EdgePluginInstallResult.Succeeded([]);
        }

        if (_compositionTransaction is not null)
        {
            return await _compositionTransaction
                .InstallAsync(
                    targets,
                    releases,
                    compatibilityHostVersion,
                    compatibilityHostApiVersion,
                    pendingHostVersion,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var installedModuleIds = new List<string>();
        var owner = targets[0].Target;
        for (var index = 0; index < releases.Count; index++)
        {
            var release = releases[index];
            var result = await _packageInstaller
                .InstallAsync(
                    owner,
                    release.Release,
                    release.CloudOptions,
                    compatibilityHostVersion,
                    compatibilityHostApiVersion,
                    CreateStepProgress(progress, index * 100 / releases.Count, releases.Count),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Success)
            {
                return result;
            }

            installedModuleIds.AddRange(result.InstalledModuleIds);
        }

        foreach (var target in targets)
        {
            if (target.ModuleIds.Count > 0)
            {
                _profileModuleConfigurationStore.EnableModules(
                    target.Target,
                    target.ModuleIds);
            }
        }

        return EdgePluginInstallResult.Succeeded(
            installedModuleIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<EdgePluginCompositionRelease> BindReleaseSources(
        IReadOnlyList<EdgePluginVersionRelease> releases,
        EdgeUpdateCloudApiOptions cloudOptions)
        => releases
            .Select(release => new EdgePluginCompositionRelease(release, cloudOptions))
            .ToArray();

    private async Task<EdgePluginInstallResult> ApplyHostReleaseWithRollbackAsync(
        EdgeHostVersionEntry release,
        bool hasPendingPluginTransaction,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var hostResult = await ApplyHostReleaseAsync(
                release,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (hostResult.Started)
            {
                return EdgePluginInstallResult.Succeeded([]);
            }

            return RollbackHostHandoffFailure(
                hasPendingPluginTransaction,
                hostResult.ErrorMessage ?? "宿主版本应用失败。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (hasPendingPluginTransaction)
            {
                var rollback = _compositionTransaction?.RollbackPendingHostHandoff();
                if (rollback is { Success: false })
                {
                    return EdgePluginInstallResult.Failed(
                        $"宿主版本应用已取消；插件/profile 回滚失败：{rollback.ErrorMessage}");
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            return RollbackHostHandoffFailure(
                hasPendingPluginTransaction,
                $"宿主版本应用失败: {ex.GetType().Name}");
        }
    }

    private EdgePluginInstallResult RollbackHostHandoffFailure(
        bool hasPendingPluginTransaction,
        string error)
    {
        if (!hasPendingPluginTransaction || _compositionTransaction is null)
        {
            return EdgePluginInstallResult.Failed(error);
        }

        var rollback = _compositionTransaction.RollbackPendingHostHandoff();
        return rollback.Success
            ? EdgePluginInstallResult.Failed(error)
            : EdgePluginInstallResult.Failed(
                $"{error}；插件/profile 回滚失败：{rollback.ErrorMessage}");
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
        var releaseSourceIssue = _releaseSourceValidator?.ValidateConfiguredSource();
        if (releaseSourceIssue is not null)
        {
            return OperationContext.Failed(
                hostVersion,
                hostApiVersion,
                releaseSourceIssue);
        }

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

        if (catalog.Success && catalog.Value is not null)
        {
            var catalogIssue = ValidateCatalog(catalog.Value);
            if (catalogIssue is not null)
            {
                return EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(catalogIssue);
            }

            var sourceIssue = _releaseSourceValidator?
                .ValidateCatalogSource(catalog.Value.HostUpdateSource);
            if (sourceIssue is not null)
            {
                return EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(sourceIssue);
            }
        }

        return catalog;
    }

    private static string? ValidateCatalog(EdgeReleaseCatalog catalog)
    {
        foreach (var release in catalog.Host.Versions)
        {
            if (!EdgeClientHostRuntime.TryParseVersion(release.Version, out _))
            {
                return $"Cloud catalog 包含非法 Host 版本: {release.Version}";
            }
        }

        foreach (var release in catalog.Plugins.SelectMany(static component => component.Versions))
        {
            if (!EdgeClientHostRuntime.TryParseVersion(release.Version, out _))
            {
                return $"Cloud catalog 包含非法插件版本: {release.Version}";
            }
        }

        return null;
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

    private static IReadOnlyList<EdgePluginVersionRelease> ResolveCompositionInstallOrder(
        IEnumerable<string> requestedModuleIds,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> releases,
        out string? issue)
    {
        var ordered = new List<EdgePluginVersionRelease>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedModuleId in requestedModuleIds)
        {
            if (!releases.TryGetValue(requestedModuleId, out var target))
            {
                issue = $"插件组合缺少 {requestedModuleId} 的发布记录。";
                return [];
            }

            var dependencyOrder = ResolveInstallOrder(target, releases, out issue);
            if (issue is not null)
            {
                return [];
            }

            foreach (var release in dependencyOrder)
            {
                if (included.Add(release.ModuleId))
                {
                    ordered.Add(release);
                }
            }
        }

        issue = null;
        return ordered;
    }

    private static IReadOnlyList<string> ResolveTargetTransactionModules(
        IReadOnlySet<string> enabledModules,
        IEnumerable<string> requestedModuleIds,
        IReadOnlyDictionary<string, EdgePluginVersionRelease> releases)
    {
        var modules = new List<string>();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedModuleId in requestedModuleIds)
        {
            if (!enabledModules.Contains(requestedModuleId)
                || !releases.TryGetValue(requestedModuleId, out var release))
            {
                continue;
            }

            var dependencyOrder = ResolveInstallOrder(release, releases, out var issue);
            if (issue is not null)
            {
                continue;
            }

            foreach (var item in dependencyOrder)
            {
                if (included.Add(item.ModuleId))
                {
                    modules.Add(item.ModuleId);
                }
            }
        }

        return modules;
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

    private sealed record CompositionTargetContext(
        EdgeUpdateTarget Target,
        OperationContext Operation,
        EdgeHostVersionEntry HostRelease,
        IReadOnlySet<string> EnabledModules);

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
