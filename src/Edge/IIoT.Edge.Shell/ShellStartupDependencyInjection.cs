using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Shell.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

internal static class ShellStartupDependencyInjection
{
    public static IServiceCollection AddShellStartupServices(this IServiceCollection services)
    {
        services.AddSingleton<ICrashLogWriter, CrashLogWriter>();
        services.AddSingleton<IShellConfigurationLoader, ShellConfigurationLoader>();
        services.AddSingleton<IShellRuntimePathResolver, ShellRuntimePathResolver>();
        services.AddSingleton<IModulePluginAssemblyResolver, ModulePluginAssemblyResolver>();
        services.AddSingleton<IModulePluginLoader, ModulePluginLoader>();
        services.AddSingleton<IModulePluginCompatibilityPolicy, ModulePluginCompatibilityPolicy>();
        services.AddSingleton<IModuleCatalog, DirectoryModuleCatalog>();
        services.AddSingleton<IShellModuleCatalog, ShellModuleCatalog>();
        return services;
    }
}
