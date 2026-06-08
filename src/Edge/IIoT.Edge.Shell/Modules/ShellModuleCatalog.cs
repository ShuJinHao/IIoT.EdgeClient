using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Modules;

public interface IShellModuleCatalog
{
    string GetPluginRootPath(string baseDirectory);

    IReadOnlyList<string> GetPluginRootPaths(string baseDirectory, string? machineProfile);

    ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath);

    ModuleCatalogDiscoveryResult DiscoverModules(IReadOnlyList<string> pluginRootPaths);

    ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules);

    IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation();

    IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation(
        IReadOnlyList<ModulePluginDescriptor> discoveredModules);

    bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules);
}

public sealed class ShellModuleCatalog : IShellModuleCatalog
{
    public const string PluginDirectoryName = "Modules";

    private readonly IModuleCatalog _moduleCatalog;

    public ShellModuleCatalog(IModuleCatalog moduleCatalog)
    {
        _moduleCatalog = moduleCatalog ?? throw new ArgumentNullException(nameof(moduleCatalog));
    }

    public string GetPluginRootPath(string baseDirectory)
        => Path.Combine(baseDirectory, PluginDirectoryName);

    public IReadOnlyList<string> GetPluginRootPaths(string baseDirectory, string? machineProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var paths = new List<string>
        {
            GetPluginRootPath(baseDirectory)
        };

        if (!string.IsNullOrWhiteSpace(machineProfile))
        {
            paths.Add(EdgeClientProgramDataPaths.ResolveProfilePluginRootPath(machineProfile, baseDirectory));
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

            if (i > 0 && !Directory.Exists(pluginRootPath))
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

    public IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation()
        => _moduleCatalog.CreateAllModules(
            DiscoverModules(GetPluginRootPaths(AppContext.BaseDirectory, null)).Modules);

    public IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation(
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.CreateAllModules(discoveredModules);

    public bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.IsDiscoveredModule(moduleId, discoveredModules);
}
