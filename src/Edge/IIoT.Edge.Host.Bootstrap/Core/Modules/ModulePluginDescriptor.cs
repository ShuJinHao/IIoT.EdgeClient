using System.Reflection;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Modules;

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

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' must implement {nameof(IEdgeProcessModule)}.");
        }

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{ModuleId}' entry type '{EntryTypeName}' must expose a public parameterless constructor.");
        }

        var instance = Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException(
                $"Failed to create plugin '{ModuleId}' from '{EntryTypeName}'.");

        return (IEdgeProcessModule)instance;
    }
}
