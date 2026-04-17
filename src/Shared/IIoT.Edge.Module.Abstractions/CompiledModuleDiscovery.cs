using System.Reflection;
using Microsoft.Extensions.DependencyModel;

namespace IIoT.Edge.Module.Abstractions;

public static class CompiledModuleDiscovery
{
    public const string DefaultAssemblyPrefix = "IIoT.Edge.Module.";

    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules(
        string assemblyPrefix = DefaultAssemblyPrefix)
        => DiscoverCompiledModules(AppDomain.CurrentDomain.GetAssemblies(), assemblyPrefix);

    public static IReadOnlyList<CompiledModuleDescriptor> DiscoverCompiledModules(
        IEnumerable<Assembly> rootAssemblies,
        string assemblyPrefix = DefaultAssemblyPrefix)
    {
        ArgumentNullException.ThrowIfNull(rootAssemblies);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPrefix);

        var descriptors = LoadCandidateAssemblies(rootAssemblies, assemblyPrefix)
            .SelectMany(CreateDescriptors)
            .OrderBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ValidateUniqueDescriptors(descriptors);
        return descriptors;
    }

    private static IReadOnlyList<Assembly> LoadCandidateAssemblies(
        IEnumerable<Assembly> rootAssemblies,
        string assemblyPrefix)
    {
        var rootAssemblyList = rootAssemblies
            .Where(static x => x is not null)
            .DistinctBy(static x => x.FullName)
            .ToArray();

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Concat(rootAssemblyList)
            .Where(static x => x is not null && !x.IsDynamic)
            .GroupBy(static x => x.GetName().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static x => x.Key,
                static x => x.First(),
                StringComparer.OrdinalIgnoreCase);

        var referencedModuleAssemblies = loadedAssemblies.Values
            .SelectMany(static assembly =>
            {
                try
                {
                    return assembly.GetReferencedAssemblies();
                }
                catch
                {
                    return [];
                }
            })
            .Concat(rootAssemblyList.SelectMany(assembly => GetDependencyContextModuleReferences(assembly, assemblyPrefix)))
            .Where(x => x.Name?.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase) == true)
            .DistinctBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var reference in referencedModuleAssemblies)
        {
            if (reference.Name is null || loadedAssemblies.ContainsKey(reference.Name))
            {
                continue;
            }

            Assembly.Load(reference);
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(x =>
                !x.IsDynamic
                && x.GetName().Name?.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(x => x.GetName().Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<AssemblyName> GetDependencyContextModuleReferences(
        Assembly assembly,
        string assemblyPrefix)
    {
        DependencyContext? dependencyContext;
        try
        {
            dependencyContext = DependencyContext.Load(assembly);
        }
        catch
        {
            yield break;
        }

        if (dependencyContext is null)
        {
            yield break;
        }

        foreach (var libraryName in dependencyContext.RuntimeLibraries
                     .Select(static x => x.Name)
                     .Concat(dependencyContext.CompileLibraries.Select(static x => x.Name))
                     .Where(x => x.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return new AssemblyName(libraryName);
        }
    }

    private static IEnumerable<CompiledModuleDescriptor> CreateDescriptors(Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unknown>";

        foreach (var moduleType in assembly
                     .GetExportedTypes()
                     .Where(type =>
                         type is { IsAbstract: false, IsInterface: false }
                         && typeof(IEdgeStationModule).IsAssignableFrom(type)
                         && type.GetConstructor(Type.EmptyTypes) is not null))
        {
            var module = (IEdgeStationModule)(Activator.CreateInstance(moduleType)
                ?? throw new InvalidOperationException(
                    $"Failed to create module instance for '{moduleType.FullName}'."));

            yield return new CompiledModuleDescriptor(
                module.ModuleId,
                module.ProcessType,
                assemblyName,
                moduleType);
        }
    }

    private static void ValidateUniqueDescriptors(IReadOnlyList<CompiledModuleDescriptor> descriptors)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            if (!moduleIds.Add(descriptor.ModuleId))
            {
                throw new InvalidOperationException(
                    $"Duplicate discovered ModuleId detected: {descriptor.ModuleId}");
            }

            if (!processTypes.Add(descriptor.ProcessType))
            {
                throw new InvalidOperationException(
                    $"Duplicate discovered ProcessType detected: {descriptor.ProcessType}");
            }
        }
    }
}
