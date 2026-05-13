using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddNavigationAvaloniaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IAvaloniaResourceContributor, NavigationAvaloniaZhCnResources>();
        services.AddSingleton<IAvaloniaResourceContributor, NavigationAvaloniaEnUsResources>();
        services.AddSingleton<IIoViewSafeInteractionPort, NoopIoViewSafeInteractionPort>();
        return services;
    }
}
