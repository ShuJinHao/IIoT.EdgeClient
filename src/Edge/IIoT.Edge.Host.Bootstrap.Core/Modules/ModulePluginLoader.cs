using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Modules;

public interface IModulePluginLoader
{
    IEdgeProcessModule CreateModule(ModulePluginDescriptor descriptor);
}

public sealed class ModulePluginLoader : IModulePluginLoader
{
    private readonly IModulePluginAssemblyResolver _assemblyResolver;

    public ModulePluginLoader(IModulePluginAssemblyResolver assemblyResolver)
    {
        _assemblyResolver = assemblyResolver ?? throw new ArgumentNullException(nameof(assemblyResolver));
    }

    public IEdgeProcessModule CreateModule(ModulePluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var assembly = _assemblyResolver.LoadAssembly(
            descriptor.EntryAssemblyPath,
            descriptor.PluginDirectory);
        var moduleType = assembly.GetType(descriptor.EntryTypeName, throwOnError: false);

        if (moduleType is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{descriptor.ModuleId}' entry type '{descriptor.EntryTypeName}' was not found in '{descriptor.AssemblyName}'.");
        }

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException(
                $"Plugin '{descriptor.ModuleId}' entry type '{descriptor.EntryTypeName}' must implement {nameof(IEdgeProcessModule)}.");
        }

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{descriptor.ModuleId}' entry type '{descriptor.EntryTypeName}' must expose a public parameterless constructor.");
        }

        var instance = Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException(
                $"Failed to create plugin '{descriptor.ModuleId}' from '{descriptor.EntryTypeName}'.");

        return (IEdgeProcessModule)instance;
    }
}
