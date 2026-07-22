using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.Bootstrap;

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

        var assembly = LoadEntryAssembly(descriptor);
        Type? moduleType;
        try
        {
            moduleType = assembly.GetType(descriptor.EntryTypeName, throwOnError: false);
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPluginLoadFailure(ex))
        {
            throw CreateLoadException(descriptor, "解析入口类型", ex);
        }

        if (moduleType is null)
        {
            throw new ModulePluginLoadException(
                $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 未在程序集 '{descriptor.AssemblyName}' 中找到。");
        }

        if (!typeof(IEdgeProcessModule).IsAssignableFrom(moduleType))
        {
            throw new ModulePluginLoadException(
                $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 必须实现 {nameof(IEdgeProcessModule)}。");
        }

        try
        {
            if (moduleType.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new ModulePluginLoadException(
                    $"插件 '{descriptor.ModuleId}' 的入口类型 '{descriptor.EntryTypeName}' 必须提供公开无参构造函数。");
            }
        }
        catch (ModulePluginLoadException)
        {
            throw;
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPluginLoadFailure(ex))
        {
            throw CreateLoadException(descriptor, "检查入口构造函数", ex);
        }

        object instance;
        try
        {
            instance = Activator.CreateInstance(moduleType)
                ?? throw new ModulePluginLoadException(
                    $"无法根据入口类型 '{descriptor.EntryTypeName}' 创建插件 '{descriptor.ModuleId}'。");
        }
        catch (ModulePluginLoadException)
        {
            throw;
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPluginLoadFailure(ex))
        {
            throw CreateLoadException(descriptor, "创建入口实例", ex);
        }

        var module = (IEdgeProcessModule)instance;
        if (!string.Equals(module.ModuleId, descriptor.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModulePluginLoadException(
                $"插件清单 ModuleId '{descriptor.ModuleId}' 与运行时入口 ModuleId '{module.ModuleId}' 不一致。");
        }

        if (!string.Equals(module.ProcessType, descriptor.ProcessType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ModulePluginLoadException(
                $"插件清单 ProcessType '{descriptor.ProcessType}' 与运行时入口 ProcessType '{module.ProcessType}' 不一致。");
        }

        return module;
    }

    private System.Reflection.Assembly LoadEntryAssembly(ModulePluginDescriptor descriptor)
    {
        try
        {
            return _assemblyResolver.LoadAssembly(
                descriptor.EntryAssemblyPath,
                descriptor.PluginDirectory);
        }
        catch (ModulePluginLoadException)
        {
            throw;
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPluginLoadFailure(ex))
        {
            throw CreateLoadException(descriptor, "装载入口程序集", ex);
        }
    }

    private static ModulePluginLoadException CreateLoadException(
        ModulePluginDescriptor descriptor,
        string operation,
        Exception innerException)
        => new(
            $"插件 '{descriptor.ModuleId}' {operation}失败：{innerException.Message}",
            innerException);
}
