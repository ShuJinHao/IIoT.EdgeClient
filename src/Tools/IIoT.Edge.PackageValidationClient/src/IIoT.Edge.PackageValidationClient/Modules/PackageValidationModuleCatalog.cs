using IIoT.Edge.Module.Abstractions;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.PackageValidationClient.Modules;

public static class PackageValidationModuleCatalog
{
    public const string DefaultModuleId = "Injection";
    private static readonly System.Reflection.Assembly[] RootAssemblies = [typeof(PackageValidationModuleCatalog).Assembly];

    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules()
        => CompiledModuleCatalog.DiscoverCompiledModules(RootAssemblies);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(IConfiguration configuration)
        => CompiledModuleCatalog.CreateEnabledModules(
            configuration,
            PackageValidationModuleOptions.SectionName,
            RootAssemblies,
            DefaultModuleId);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<CompiledModuleDescriptor> compiledModules)
        => CompiledModuleCatalog.CreateEnabledModules(
            configuration,
            PackageValidationModuleOptions.SectionName,
            compiledModules,
            DefaultModuleId);
}

