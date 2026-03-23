using IIoT.Edge.UI.Shared.Widgets.Footer;
using IIoT.Edge.UI.Shared.Widgets.Login;
using IIoT.Edge.UI.Shared.Widgets.SysMenu;
using IIoT.Edge.UI.Shared.Widgets.SystemHeader;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.UI.Shared;

public static class DependencyInjection
{
    public static IServiceCollection AddShellWidgets(
        this IServiceCollection services)
    {
        services.AddSingleton<HeaderWidget>();
        services.AddSingleton<SysMenuWidget>();
        services.AddSingleton<LoginWidget>();
        services.AddSingleton<FooterWidget>();

        return services;
    }
}