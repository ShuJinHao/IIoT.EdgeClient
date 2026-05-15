using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.Edge.UI.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddAvaloniaUiShared(this IServiceCollection services)
    {
        services.TryAddSingleton<IAvaloniaViewRegistry, AvaloniaViewRegistry>();
        services.TryAddSingleton<IAvaloniaNavigationService, AvaloniaNavigationService>();
        services.TryAddSingleton<IAvaloniaDispatcherService, AvaloniaDispatcherService>();
        services.TryAddSingleton<IAvaloniaDialogService, AvaloniaDialogService>();
        services.TryAddSingleton<IAvaloniaTimerFactory, AvaloniaTimerFactory>();
        services.TryAddSingleton<IAvaloniaWindowService, AvaloniaWindowService>();
        services.TryAddSingleton<IAvaloniaRuntimeState, AvaloniaRuntimeState>();
        services.TryAddSingleton<IAvaloniaCsvExportService, AvaloniaCsvExportService>();
        return services;
    }
}
