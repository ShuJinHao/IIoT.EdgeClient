using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Module.Abstractions;

public static class DirectoryModuleCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRootPath);

        if (!Directory.Exists(pluginRootPath))
        {
            return new ModuleCatalogDiscoveryResult(
                [],
                [
                    new ModuleCatalogIssue(
                        "PLUGIN_ROOT_MISSING",
                        $"Plugin root directory '{pluginRootPath}' was not found.")
                ]);
        }

        var descriptors = new List<ModulePluginDescriptor>();
        var issues = new List<ModuleCatalogIssue>();
        foreach (var pluginDirectory in Directory.EnumerateDirectories(pluginRootPath))
        {
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                descriptors.Add(LoadDescriptor(pluginDirectory, manifestPath));
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

    public static ModuleCatalogActivationResult CreateEnabledModules(
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
        var modules = new List<IEdgeStationModule>(configuredEnabledModuleIds.Count);
        var pendingDescriptors = new Dictionary<string, ModulePluginDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var moduleId in configuredEnabledModuleIds)
        {
            if (!modulesById.TryGetValue(moduleId, out var descriptor))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_ENABLED_NOT_FOUND",
                    $"Unknown module configured in {sectionName}:Enabled: {moduleId}",
                    moduleId));
                continue;
            }

            if (!IsHostCompatible(descriptor, out var compatibilityMessage))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_HOST_VERSION_INCOMPATIBLE",
                    compatibilityMessage,
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
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
                $"Plugin '{descriptor.ModuleId}' requires enabled dependencies: {string.Join(", ", missingDependencies)}.",
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
                    modules.Add(descriptor.CreateModule());
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
                    $"Plugin '{descriptor.ModuleId}' cannot be activated because dependencies are unavailable: {string.Join(", ", unresolvedDependencies)}.",
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
                $"No enabled plugins could be loaded from section '{sectionName}'."));
        }

        return new ModuleCatalogActivationResult(modules, configuredEnabledModuleIds, issues);
    }

    public static IReadOnlyList<IEdgeStationModule> CreateAllModules(IReadOnlyList<ModulePluginDescriptor> discoveredModules)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        return discoveredModules.Select(static x => x.CreateModule()).ToArray();
    }

    public static bool IsDiscoveredModule(
        string moduleId,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
    {
        ArgumentNullException.ThrowIfNull(discoveredModules);
        return !string.IsNullOrWhiteSpace(moduleId)
            && discoveredModules.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ResolveEnabledModuleIds(
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
                    $"Duplicate module id configured in {sectionName}:Enabled: {moduleId}",
                    moduleId));
                continue;
            }

            result.Add(moduleId);
        }

        duplicateIssues = issues;
        return result;
    }

    private static ModulePluginDescriptor LoadDescriptor(string pluginDirectory, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ModulePluginManifest>(
            File.ReadAllText(manifestPath),
            JsonOptions)
            ?? throw new InvalidOperationException(
                $"Plugin manifest '{manifestPath}' could not be parsed.");

        ValidateManifest(manifest, manifestPath);

        var entryAssemblyPath = Path.Combine(pluginDirectory, manifest.EntryAssembly);
        if (!File.Exists(entryAssemblyPath))
        {
            throw new InvalidOperationException(
                $"Plugin '{manifest.ModuleId}' entry assembly '{manifest.EntryAssembly}' was not found at '{entryAssemblyPath}'.");
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

    private static void ValidateManifest(ModulePluginManifest manifest, string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifest.ModuleId))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing moduleId.");
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing displayName.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing version.");
        }

        if (!ModulePluginHostRuntime.TryParseVersion(manifest.Version, out _))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' has an invalid version: {manifest.Version}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.HostApiVersion))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing hostApiVersion.");
        }

        if (string.IsNullOrWhiteSpace(manifest.MinHostVersion)
            || !ModulePluginHostRuntime.TryParseVersion(manifest.MinHostVersion, out _))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' has an invalid minHostVersion: {manifest.MinHostVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.MaxHostVersion)
            || !ModulePluginHostRuntime.TryParseVersion(manifest.MaxHostVersion, out _))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' has an invalid maxHostVersion: {manifest.MaxHostVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.SupportedProcessType))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing supportedProcessType.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing entryAssembly.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            throw new InvalidOperationException($"Plugin manifest '{manifestPath}' is missing entryType.");
        }
    }

    private static bool IsHostCompatible(ModulePluginDescriptor descriptor, out string message)
    {
        if (!string.Equals(
                descriptor.HostApiVersion,
                ModulePluginHostRuntime.HostApiVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            message =
                $"Plugin '{descriptor.ModuleId}' targets host API {descriptor.HostApiVersion}, but the current host API is {ModulePluginHostRuntime.HostApiVersion}.";
            return false;
        }

        _ = ModulePluginHostRuntime.TryParseVersion(descriptor.MinHostVersion, out var minVersion);
        _ = ModulePluginHostRuntime.TryParseVersion(descriptor.MaxHostVersion, out var maxVersion);
        _ = ModulePluginHostRuntime.TryParseVersion(ModulePluginHostRuntime.HostVersion, out var hostVersion);

        if (hostVersion < minVersion || hostVersion > maxVersion)
        {
            message =
                $"Plugin '{descriptor.ModuleId}' supports host versions {descriptor.MinHostVersion} - {descriptor.MaxHostVersion}, but the current host version is {ModulePluginHostRuntime.HostVersion}.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static IReadOnlyList<ModuleCatalogIssue> ValidateUniqueDescriptors(IReadOnlyList<ModulePluginDescriptor> descriptors)
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
                    $"Duplicate discovered module id detected: {descriptor.ModuleId}",
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
            }

            if (!processTypes.Add(descriptor.ProcessType))
            {
                issues.Add(new ModuleCatalogIssue(
                    "PLUGIN_DISCOVERY_DUPLICATE_PROCESS",
                    $"Duplicate discovered process type detected: {descriptor.ProcessType}",
                    descriptor.ModuleId,
                    descriptor.ManifestPath,
                    descriptor.EntryAssemblyPath,
                    Path.GetFileName(descriptor.PluginDirectory)));
            }
        }

        return issues;
    }
}
