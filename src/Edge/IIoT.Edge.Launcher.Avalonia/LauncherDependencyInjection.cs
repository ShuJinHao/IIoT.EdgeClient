using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Launcher;

public static class LauncherDependencyInjection
{
    public static IServiceCollection AddLauncherServices(
        this IServiceCollection services,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        services.AddSingleton<ILauncherAccountCatalogInitializer>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalogInitializer>(provider, baseDirectory));
        services.AddSingleton<ILauncherAccountCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherAccountCatalog>(provider, baseDirectory));
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherProfileCatalog>(provider, baseDirectory));
        services.AddSingleton<ILocalLauncherAuthService, LocalLauncherAuthService>();
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<LauncherMainViewModel>();

        return services;
    }
}
