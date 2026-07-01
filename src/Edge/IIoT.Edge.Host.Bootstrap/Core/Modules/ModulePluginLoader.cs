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
                $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 未在程序集 '{descriptor.AssemblyName}' 中找到。");
        }

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(moduleType))
        {
            throw new InvalidOperationException(
                $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 必须实现 {nameof(IEdgeProcessModule)}。");
        }

        if (moduleType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 必须提供公开无参构造函数。");
        }

        var instance = Activator.CreateInstance(moduleType)
            ?? throw new InvalidOperationException(
                $"无法根据入口类型 '{descriptor.EntryTypeName}' 创建插件 '{descriptor.ModuleId}'。");

        return (IEdgeProcessModule)instance;
    }
}
