using IIoT.Edge.Presentation.Shell.Avalonia.Features.SysMenu.ViewModels;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Shell.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddShellAvaloniaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<HeaderViewModel>();
        services.AddSingleton<FooterViewModel>();
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<SysMenuViewModel>();
        return services;
    }
}
