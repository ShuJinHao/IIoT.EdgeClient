using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Modules;

public interface IShellModuleCatalog
{
    string GetPluginRootPath(string baseDirectory);

    IReadOnlyList<string> GetPluginRootPaths(string baseDirectory, IConfiguration configuration);

    ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath);

    ModuleCatalogDiscoveryResult DiscoverModules(IReadOnlyList<string> pluginRootPaths);

    ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules);

    bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules);
}

public sealed class ShellModuleCatalog : IShellModuleCatalog
{
    private readonly IModuleCatalog _moduleCatalog;

    public ShellModuleCatalog(IModuleCatalog moduleCatalog)
    {
        _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
    }

    public string GetPluginRootPath(string baseDirectory)
        => EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory);

    public IReadOnlyList<string> GetPluginRootPaths(string baseDirectory, IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredRoots = configuration
            .GetSection($"{ShellModuleOptions.SectionName}:PluginRoots")
            .Get<string[]>()
            ?? [];
        var paths = new List<string>();
        foreach (var configuredRoot in configuredRoots.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                paths.Add(ResolveConfiguredPluginRoot(baseDirectory, configuredRoot));
            }
            catch
            {
                // ShellConfigurationLoader records the detailed startup diagnostic.
                // Catalog resolution must remain non-blocking even when invoked independently.
            }
        }
        if (paths.Count == 0)
        {
            paths.Add(GetPluginRootPath(baseDirectory));
        }

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
        => _moduleCatalog.DiscoverModules(pluginRootPath);

    public ModuleCatalogDiscoveryResult DiscoverModules(IReadOnlyList<string> pluginRootPaths)
    {
        ArgumentNullException.ThrowIfNull(pluginRootPaths);

        var selectedModules = new List<ModulePluginDescriptor>();
        var issues = new List<ModuleCatalogIssue>();
        for (var i = 0; i < pluginRootPaths.Count; i++)
        {
            var pluginRootPath = pluginRootPaths[i];
            if (string.IsNullOrWhiteSpace(pluginRootPath))
            {
                continue;
            }

            if (!Directory.Exists(pluginRootPath))
            {
                continue;
            }

            var discovery = _moduleCatalog.DiscoverModules(pluginRootPath);
            issues.AddRange(discovery.Issues);
            foreach (var descriptor in discovery.Modules)
            {
                selectedModules.RemoveAll(existing =>
                    string.Equals(existing.ModuleId, descriptor.ModuleId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existing.ProcessType, descriptor.ProcessType, StringComparison.OrdinalIgnoreCase));
                selectedModules.Add(descriptor);
            }
        }

        return new ModuleCatalogDiscoveryResult(
            selectedModules
                .OrderBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            issues);
    }

    public ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.CreateEnabledModules(
            configuration,
            ShellModuleOptions.SectionName,
            discoveredModules);

    public bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.IsDiscoveredModule(moduleId, discoveredModules);

    private static string ResolveConfiguredPluginRoot(string baseDirectory, string path)
        => EdgeClientProgramDataPaths.ResolveConfiguredPluginRoot(baseDirectory, path);
}
