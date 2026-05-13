using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.UI.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddAvaloniaUiShared(this IServiceCollection services)
    {
        services.AddSingleton<IAvaloniaViewRegistry, AvaloniaViewRegistry>();
        services.AddSingleton<IAvaloniaNavigationService, AvaloniaNavigationService>();
        services.AddSingleton<IAvaloniaDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IAvaloniaDialogService, AvaloniaDialogService>();
        return services;
    }
}
