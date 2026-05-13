using IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddPanelAvaloniaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<EquipmentViewModel>();
        services.AddSingleton<LogViewModel>();
        return services;
    }
}
