using System.Reflection;
using IIoT.Edge.Plugin.Shared.Modules;

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
    public IEdgeProcessModule CreateModule()
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

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(moduleType)
            && !typeof(IEdgeStationModule).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' does not implement a supported module contract.");
        }

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' must expose a public parameterless constructor.");
        }

        var instance = Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException(
                $"Failed to create plugin '{ModuleId}' from '{EntryTypeName}'.");

        return instance switch
        {
            IEdgeProcessModule processModule => processModule,
            IEdgeStationModule stationModule => new LegacyEdgeStationModuleAdapter(stationModule, DisplayName),
            _ => throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' produced an unsupported module instance.")
        };
    }
}
