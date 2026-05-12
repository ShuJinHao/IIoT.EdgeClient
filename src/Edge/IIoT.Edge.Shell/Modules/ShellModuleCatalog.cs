using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.Bootstrap.Modules;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Modules;

public interface IShellModuleCatalog
{
    string GetPluginRootPath(string baseDirectory);

    ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath);

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

    public ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
        => _moduleCatalog.DiscoverModules(pluginRootPath);

    public ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.CreateEnabledModules(
            configuration,
            ShellModuleOptions.SectionName,
            discoveredModules);

    public IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation()
        => _moduleCatalog.CreateAllModules(
            DiscoverModules(GetPluginRootPath(AppContext.BaseDirectory)).Modules);

    public IReadOnlyList<IEdgeProcessModule> CreateAllModulesForValidation(
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.CreateAllModules(discoveredModules);

    public bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => _moduleCatalog.IsDiscoveredModule(moduleId, discoveredModules);
}
