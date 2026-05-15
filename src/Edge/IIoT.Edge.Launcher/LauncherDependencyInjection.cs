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
        services.AddLauncherCore(baseDirectory);
        services.AddSingleton<MainWindow>();

        return services;
    }
}
