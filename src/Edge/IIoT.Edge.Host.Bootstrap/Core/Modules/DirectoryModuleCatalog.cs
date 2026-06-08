using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public interface IModuleCatalog
{
    ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath);

    ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        string sectionName,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules);

    IReadOnlyList<IEdgeProcessModule> CreateAllModules(IReadOnlyList<ModulePluginDescriptor> discoveredModules);

    bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules);
}

public sealed class DirectoryModuleCatalog : IModuleCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IModulePluginLoader _modulePluginLoader;
    private readonly IModulePluginCompatibilityPolicy _compatibilityPolicy;

    public DirectoryModuleCatalog(
        IModulePluginLoader modulePluginLoader,
        IModulePluginCompatibilityPolicy compatibilityPolicy)
    {
        _modulePluginLoader = modulePluginLoader ?? throw new ArgumentNullException(nameof(modulePluginLoader));
        _compatibilityPolicy = compatibilityPolicy ?? throw new ArgumentNullException(nameof(compatibilityPolicy));
    }

    public ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRootPath);

        if (!Directory.Exists(pluginRootPath))
        {
            return new ModuleCatalogDiscoveryResult(
                [],
                [
                    new ModuleCatalogIssue(
                        "PLUGIN_ROOT_MISSING",
                        $"插件根目录“{pluginRootPath}”不存在。")
                ]);
        }

        var descriptors = new List<ModulePluginDescriptor>();
        var issues = new List<ModuleCatalogIssue>();
        foreach (var pluginDirectory in Directory.EnumerateDirectories(pluginRootPath))
        {
            var manifestPath = ResolvePluginManifestPath(pluginDirectory);
            if (manifestPath is null)
            {
                continue;
            }

            try
            {
                descriptors.Add(LoadDescriptor(Path.GetDirectoryName(manifestPath)!, manifestPath));
            }
            catch (Exception ex)
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_MANIFEST_INVALID",
                    ex.Message,
                    ManifestPath: manifestPath,
                    PluginDirectoryName: Path.GetFileName(pluginDirectory)));
            }
        }

        issues.AddRange(ValidateUniqueDescriptors(descriptors));

        var validDescriptors = descriptors
            .GroupBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .GroupBy(static x => x.ProcessType, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ModuleCatalogDiscoveryResult(validDescriptors, issues);
    }

    private static string? ResolvePluginManifestPath(string pluginDirectory)
    {
        var directManifestPath = Path.Combine(pluginDirectory, "plugin.json");
        if (File.Exists(directManifestPath))
        {
            return directManifestPath;
        }

        var currentManifestPath = Path.Combine(
            pluginDirectory,
            "current",
            "plugin.json");
        return File.Exists(currentManifestPath)
            ? currentManifestPath
            : null;
    }

    public ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        string sectionName,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(discoveredModules);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var configuredEnabledModuleIds = ResolveEnabledModuleIds(configuration, sectionName, discoveredModules, out var duplicateIssues);
        var issues = new List<ModuleCatalogIssue>(duplicateIssues);
        var modulesById = discoveredModules.ToDictionary(
            static x => x.ModuleId,
            StringComparer.OrdinalIgnoreCase);
        var modules = new List<IEdgeProcessModule>(configuredEnabledModuleIds.Count);
        var pendingDescriptors = new Dictionary<string, ModulePluginDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleId in configuredEnabledModuleIds)
        {
            if (!modulesById.TryGetValue(moduleId, out var descriptor))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_ENABLED_NOT_FOUND",
                    $"{sectionName}:Enabled 配置了未知模块：{moduleId}",
                    moduleId));
                continue;
            }

            var compatibility = _compatibilityPolicy.Evaluate(descriptor);
            if (!compatibility.IsCompatible)
            {
                issues.Add(compatibility.Issue!);
                continue;
            }

            pendingDescriptors.Add(descriptor.ModuleId, descriptor);
        }

        foreach (var descriptor in pendingDescriptors.Values.ToArray())
        {
            var missingDependencies = descriptor.Dependencies
                .Where(dependency =>
                    !modulesById.ContainsKey(dependency)
                    || !configuredEnabledModuleIds.Contains(dependency, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            if (missingDependencies.Length == 0)
            {
                continue;
            }

            issues.Add(new ModuleCatalogIssue(
                "PLUGIN_DEPENDENCY_MISSING",
                $"插件“{descriptor.ModuleId}”依赖的模块未启用：{string.Join(", ", missingDependencies)}。",
                descriptor.ModuleId,
                descriptor.ManifestPath,
                descriptor.EntryAssemblyPath,
                Path.GetFileName(descriptor.PluginDirectory)));
            pendingDescriptors.Remove(descriptor.ModuleId);
        }

        var activatedModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pendingDescriptors.Count > 0)
        {
            var progressMade = false;
            foreach (var descriptor in pendingDescriptors.Values.ToArray())
            {
                if (descriptor.Dependencies.Any()
                    && descriptor.Dependencies.Any(dependency => !activatedModuleIds.Contains(dependency)))
                {
                    continue;
                }

                try
                {
                    modules.Add(_modulePluginLoader.CreateModule(descriptor));
                    activatedModuleIds.Add(descriptor.ModuleId);
                    pendingDescriptors.Remove(descriptor.ModuleId);
                    progressMade = true;
                }
                catch (Exception ex)
                {
                    issues.Add(new ModuleCatalogIssue(
                        "PLUGIN_LOAD_FAILED",
                        ex.Message,
                        descriptor.ModuleId,
                        descriptor.ManifestPath,
                        descriptor.EntryAssemblyPath,
                        Path.GetFileName(descriptor.PluginDirectory)));
                    pendingDescriptors.Remove(descriptor.ModuleId);
                }
            }

            if (progressMade)
            {
                continue;
            }

            foreach (var descriptor in pendingDescriptors.Values)
            {
                var unresolvedDependencies = descriptor.Dependencies
                    .Where(dependency => !activatedModuleIds.Contains(dependency))
                    .ToArray();

                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_DEPENDENCY_MISSING",
                    $"插件“{descriptor.ModuleId}”无法激活，依赖模块不可用：{string.Join(", ", unresolvedDependencies)}。",
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
            }

            pendingDescriptors.Clear();
        }

        if (modules.Count == 0)
        {
            issues.Add(new ModuleCatalogIssue(
                "PLUGIN_NONE_ENABLED",
                $"未能从配置节“{sectionName}”加载任何已启用插件。"));
        }

        return new ModuleCatalogActivationResult(modules, configuredEnabledModuleIds, issues);
    }

    public IReadOnlyList<IEdgeProcessModule> CreateAllModules(IReadOnlyList<ModulePluginDescriptor> discoveredModules)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        return discoveredModules.Select(_modulePluginLoader.CreateModule).ToArray();
    }

    public bool IsDiscoveredModule(
        string moduleId,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        return !string.IsNullOrWhiteSpace(moduleId)
            && discoveredModules.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<string> ResolveEnabledModuleIds(
        IConfiguration configuration,
        string sectionName,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules,
        out IReadOnlyList<ModuleCatalogIssue> duplicateIssues)
    {
        var configuredValues = configuration
            .GetSection($"{sectionName}:Enabled")
            .Get<string[]>()
            ?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList()
            ?? [];

        if (configuredValues.Count == 0)
        {
            configuredValues.AddRange(discoveredModules.Select(static x => x.ModuleId));
        }

        var uniqueModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(configuredValues.Count);
        var issues = new List<ModuleCatalogIssue>();
        foreach (var moduleId in configuredValues)
        {
            if (!uniqueModuleIds.Add(moduleId))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_ENABLED_DUPLICATE",
                    $"{sectionName}:Enabled 中重复配置了模块：{moduleId}",
                    moduleId));
                continue;
            }

            result.Add(moduleId);
        }

        duplicateIssues = issues;
        return result;
    }

    private ModulePluginDescriptor LoadDescriptor(string pluginDirectory, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ModulePluginManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions)
            ?? throw new InvalidOperationException(
                $"插件清单“{manifestPath}”无法解析。");

        ValidateManifest(manifest, manifestPath);

        var entryAssemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            throw new InvalidOperationException(
                $"插件“{manifest.ModuleId}”的入口程序集“{manifest.EntryAssembly}”不存在：{entryAssemblyPath}。");
        }

        return new ModulePluginDescriptor(
            manifest.ModuleId,
            manifest.SupportedProcessType,
            manifest.DisplayName,
            manifest.Version,
            manifest.HostApiVersion,
            manifest.MinHostVersion,
            manifest.MaxHostVersion,
            manifest.Dependencies
                .Where(static dependency => !string.IsNullOrWhiteSpace(dependency))
                .Select(static dependency => dependency.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Path.GetFileNameWithoutExtension(manifest.EntryAssembly),
            manifest.EntryType,
            pluginDirectory,
            manifestPath,
            entryAssemblyPath);
    }

    private void ValidateManifest(ModulePluginManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.ModuleId))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 moduleId。");
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 displayName。");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 version。");
        }

        if (!ModulePluginHostRuntime.TryParseVersion(manifest.Version, out _))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”的 version 无效：{manifest.Version}。");
        }

        if (string.IsNullOrWhiteSpace(manifest.HostApiVersion))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 hostApiVersion。");
        }

        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion)
            || !ModulePluginHostRuntime.TryParseVersion(manifest.MinHostVersion, out _))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”的 minHostVersion 无效：{manifest.MinHostVersion}。");
        }

        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion)
            || !ModulePluginHostRuntime.TryParseVersion(manifest.MaxHostVersion, out _))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”的 maxHostVersion 无效：{manifest.MaxHostVersion}。");
        }

        if (string.IsNullOrWhiteSpace(manifest.SupportedProcessType))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 supportedProcessType。");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 entryAssembly。");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            throw new InvalidOperationException($"插件清单“{manifestPath}”缺少 entryType。");
        }
    }

    private IReadOnlyList<ModuleCatalogIssue> ValidateUniqueDescriptors(IReadOnlyList<ModulePluginDescriptor> descriptors)
    {
        var issues = new List<ModuleCatalogIssue>();
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            if (!moduleIds.Add(descriptor.ModuleId))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_DISCOVERY_DUPLICATE_MODULE",
                    $"发现重复的模块标识：{descriptor.ModuleId}",
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
            }

            if (!processTypes.Add(descriptor.ProcessType))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_DISCOVERY_DUPLICATE_PROCESS",
                    $"发现重复的工序类型：{descriptor.ProcessType}",
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
            }
        }

        return issues;
    }
}
