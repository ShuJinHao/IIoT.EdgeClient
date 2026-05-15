using IIoT.Edge.Presentation.Shell.Avalonia.Localization;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Shell.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddShellAvaloniaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IAvaloniaResourceContributor, ShellAvaloniaZhCnResources>();
        services.AddSingleton<IAvaloniaResourceContributor, ShellAvaloniaEnUsResources>();
        services.AddSingleton<HeaderViewModel>();
        services.AddSingleton<FooterViewModel>();
        services.AddSingleton<LoginViewModel>();
        return services;
    }
}
