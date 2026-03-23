using IIoT.Edge.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public static class DependencyInjection
{
    public static IServiceCollection AddShell(
        this IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}