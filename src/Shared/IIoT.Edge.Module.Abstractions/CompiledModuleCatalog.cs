using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Abstractions;

public static class CompiledModuleCatalog
{
    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules(
        string assemblyPrefix = CompiledModuleDiscovery.DefaultAssemblyPrefix)
        => CompiledModuleDiscovery.DiscoverCompiledModules(assemblyPrefix);

    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules(
        IEnumerable<System.Reflection.Assembly> rootAssemblies,
        string assemblyPrefix = CompiledModuleDiscovery.DefaultAssemblyPrefix)
        => CompiledModuleDiscovery.DiscoverCompiledModules(rootAssemblies, assemblyPrefix);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(
        IConfiguration configuration,
        string sectionName,
        string defaultModuleId)
        => CreateEnabledModules(
            configuration,
            sectionName,
            DiscoverCompiledModules(),
            defaultModuleId);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(
        IConfiguration configuration,
        string sectionName,
        IEnumerable<System.Reflection.Assembly> rootAssemblies,
        string defaultModuleId)
        => CreateEnabledModules(
            configuration,
            sectionName,
            DiscoverCompiledModules(rootAssemblies),
            defaultModuleId);

    public static IReadOnlyList<IEdgeStationModule> CreateEnabledModules(
        IConfiguration configuration,
        string sectionName,
        IReadOnlyList<CompiledModuleDescriptor> compiledModules,
        string defaultModuleId)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentNullException.ThrowIfNull(compiledModules);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModuleId);

        var enabledModuleIds = configuration
            .GetSection($"{sectionName}:Enabled")
            .Get<string[]>()
            ?.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList()
            ?? [];

        if (enabledModuleIds.Count == 0)
        {
            enabledModuleIds.Add(defaultModuleId);
        }

        var compiledModulesById = compiledModules.ToDictionary(
            static x => x.ModuleId,
            StringComparer.OrdinalIgnoreCase);
        var uniqueModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledModules = new List<IEdgeStationModule>(enabledModuleIds.Count);

        foreach (var moduleId in enabledModuleIds)
        {
            if (!uniqueModuleIds.Add(moduleId))
            {
                throw new InvalidOperationException(
                    $"Duplicate module id configured in {sectionName}:Enabled: {moduleId}");
            }

            if (!compiledModulesById.TryGetValue(moduleId, out var descriptor))
            {
                throw new InvalidOperationException(
                    $"Unknown module configured in {sectionName}:Enabled: {moduleId}");
            }

            enabledModules.Add(descriptor.CreateModule());
        }

        return enabledModules;
    }

    public static IReadOnlyList<IEdgeStationModule> CreateAllModules()
        => CreateAllModules(DiscoverCompiledModules());

    public static IReadOnlyList<IEdgeStationModule> CreateAllModules(
        IEnumerable<System.Reflection.Assembly> rootAssemblies)
        => CreateAllModules(DiscoverCompiledModules(rootAssemblies));

    public static IReadOnlyList<IEdgeStationModule> CreateAllModules(
        IReadOnlyList<CompiledModuleDescriptor> compiledModules)
    {
        ArgumentNullException.ThrowIfNull(compiledModules);
        return compiledModules.Select(static x => x.CreateModule()).ToList();
    }

    public static bool IsCompiledModule(string moduleId)
        => IsCompiledModule(moduleId, DiscoverCompiledModules());

    public static bool IsCompiledModule(
        string moduleId,
        IEnumerable<System.Reflection.Assembly> rootAssemblies)
        => IsCompiledModule(moduleId, DiscoverCompiledModules(rootAssemblies));

    public static bool IsCompiledModule(string moduleId, IReadOnlyList<CompiledModuleDescriptor> compiledModules)
    {
        ArgumentNullException.ThrowIfNull(compiledModules);
        return !string.IsNullOrWhiteSpace(moduleId)
            && compiledModules.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase));
    }
}
