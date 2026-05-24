using IIoT.Edge.Application.Auth.LocalAccounts;
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

        services.AddSingleton<ILocalAccountCatalogInitializer>(
            _ => new LocalAccountCatalogInitializer(baseDirectory));
        services.AddSingleton<ILocalAccountCatalog>(
            _ => new LocalAccountCatalog(baseDirectory));
        services.AddSingleton<ILocalAccountAuthService, LocalAccountAuthService>();
        services.AddSingleton<ILauncherProfileCatalog>(
            provider => ActivatorUtilities.CreateInstance<LauncherProfileCatalog>(provider, baseDirectory));
        services.AddSingleton<IProcessStarter, ProcessStarter>();
        services.AddSingleton<IShellLaunchService, ShellLaunchService>();
        services.AddSingleton<LauncherMainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
