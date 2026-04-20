using IIoT.Edge.Module.Abstractions;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.PackageValidationClient.Modules;

public static class PackageValidationModuleCatalog
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
            PackageValidationModuleOptions.SectionName,
            discoveredModules);
}
