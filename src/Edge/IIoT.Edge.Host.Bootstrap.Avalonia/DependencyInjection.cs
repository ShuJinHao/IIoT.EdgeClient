using IIoT.Edge.Host.Bootstrap.Core;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Panels.Avalonia;
using IIoT.Edge.Presentation.Shell.Avalonia;
using IIoT.Edge.UI.Avalonia;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Host.Bootstrap.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeHostAvaloniaBootstrap(
        this IServiceCollection services,
        AvaloniaHostBootstrapOptions options)
    {
        services.AddEdgeHostCoreServices(new EdgeHostCoreOptions(
            options.Configuration,
            options.RuntimePaths,
            options.EnvironmentName));
        services.AddAvaloniaUiShared();
        services.AddShellAvaloniaPresentation();
        services.AddPanelAvaloniaPresentation();
        services.AddNavigationAvaloniaPresentation();
        services.AddSingleton(options);
        services.AddSingleton<IAvaloniaLanguageService>(sp =>
            new AvaloniaResourceLanguageService(sp.GetServices<IAvaloniaResourceContributor>()));
        return services;
    }

    public static void RegisterAvaloniaViews(IServiceProvider services)
    {
        ShellAvaloniaPresentationRegistration.RegisterShellViews(services);
        PanelAvaloniaPresentationRegistration.RegisterPanelViews(services);
        NavigationAvaloniaPresentationRegistration.RegisterNavigationViews(
            services,
            services.GetRequiredService<AvaloniaHostBootstrapOptions>().ModuleIds);
    }
}
