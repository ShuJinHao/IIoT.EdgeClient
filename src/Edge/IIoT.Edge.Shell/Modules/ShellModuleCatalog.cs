using IIoT.Edge.Module.Abstractions;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Modules;

public static class ShellModuleCatalog
{
    public const string DefaultModuleId = "Injection";
    private static readonly System.Reflection.Assembly[] RootAssemblies = [typeof(ShellModuleCatalog).Assembly];

    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules()
        => CompiledModuleCatalog.DiscoverCompiledModules(RootAssemblies);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(IConfiguration configuration)
        => CompiledModuleCatalog.CreateEnabledModules(
            configuration,
            ShellModuleOptions.SectionName,
            RootAssemblies,
            DefaultModuleId);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(
        IConfiguration configuration,
        IReadOnlyList<CompiledModuleDescriptor> compiledModules)
        => CompiledModuleCatalog.CreateEnabledModules(
            configuration,
            ShellModuleOptions.SectionName,
            compiledModules,
            DefaultModuleId);

    public static IReadOnlyList<IEdgeStationModule> CreateAllModulesForValidation()
        => CompiledModuleCatalog.CreateAllModules(RootAssemblies);

    public static IReadOnlyList<IEdgeStationModule> CreateAllModulesForValidation(
        IReadOnlyList<CompiledModuleDescriptor> compiledModules)
        => CompiledModuleCatalog.CreateAllModules(compiledModules);

    public static bool IsCompiledModule(string moduleId)
        => CompiledModuleCatalog.IsCompiledModule(moduleId, RootAssemblies);

    public static bool IsCompiledModule(string moduleId, IReadOnlyList<CompiledModuleDescriptor> compiledModules)
        => CompiledModuleCatalog.IsCompiledModule(moduleId, compiledModules);
}
