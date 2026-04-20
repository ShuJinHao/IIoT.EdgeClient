using IIoT.Edge.Module.Abstractions;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Modules;

public static class ShellModuleCatalog
{
    public const string PluginDirectoryName = "Modules";

    public static string GetPluginRootPath(string baseDirectory)
        => Path.Combine(baseDirectory, PluginDirectoryName);

    public static ModuleCatalogDiscoveryResult DiscoverModules(string pluginRootPath)
        => DirectoryModuleCatalog.DiscoverModules(pluginRootPath);

    public static ModuleCatalogActivationResult CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => DirectoryModuleCatalog.CreateEnabledModules(
            configuration,
            ShellModuleOptions.SectionName,
            discoveredModules);

    public static IReadOnlyList<IEdgeStationModule> CreateAllModulesForValidation()
        => DirectoryModuleCatalog.CreateAllModules(
            DiscoverModules(GetPluginRootPath(AppContext.BaseDirectory)).Modules);

    public static IReadOnlyList<IEdgeStationModule> CreateAllModulesForValidation(
        IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => DirectoryModuleCatalog.CreateAllModules(discoveredModules);

    public static bool IsDiscoveredModule(string moduleId, IReadOnlyList<ModulePluginDescriptor> discoveredModules)
        => DirectoryModuleCatalog.IsDiscoveredModule(moduleId, discoveredModules);
}
