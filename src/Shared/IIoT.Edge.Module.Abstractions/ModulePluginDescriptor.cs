using System.Reflection;

namespace IIoT.Edge.Module.Abstractions;

public sealed record ModulePluginDescriptor(
    string ModuleId,
    string ProcessType,
    string DisplayName,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    IReadOnlyList<string> Dependencies,
    string AssemblyName,
    string EntryTypeName,
    string PluginDirectory,
    string ManifestPath,
    string EntryAssemblyPath)
{
    public IEdgeStationModule CreateModule()
    {
        var assembly = ModulePluginAssemblyResolver.LoadAssembly(
            EntryAssemblyPath,
            PluginDirectory);
        var moduleType = assembly.GetType(EntryTypeName, throwOnError: false);

        if (moduleType is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' was not found in '{AssemblyName}'.");
        }

        if (!typeof(IEdgeStationModule).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' does not implement {nameof(IEdgeStationModule)}.");
        }

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' must expose a public parameterless constructor.");
        }

        return (IEdgeStationModule)(Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException(
                $"Failed to create plugin '{ModuleId}' from '{EntryTypeName}'."));
    }
}
