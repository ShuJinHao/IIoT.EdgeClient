using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Shell.Avalonia;

public static class ShellAvaloniaPresentationRegistration
{
    public static void RegisterShellViews(IServiceProvider services)
    {
        _ = services.GetRequiredService<HeaderViewModel>();
        _ = services.GetRequiredService<FooterViewModel>();
        _ = services.GetRequiredService<LoginViewModel>();
    }
}
