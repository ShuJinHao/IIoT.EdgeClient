using IIoT.Edge.AvaloniaShell.Localization;
using IIoT.Edge.AvaloniaShell.ViewModels;
using IIoT.Edge.AvaloniaShell.Views;
using IIoT.Edge.UI.Avalonia;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.AvaloniaShell;

public static class ShellAvaloniaRegistration
{
    public static IServiceCollection AddAvaloniaShell(this IServiceCollection services)
    {
        services.AddAvaloniaUiShared();
        services.AddSingleton<IAvaloniaLanguageService>(_ => new AvaloniaResourceLanguageService(ShellLanguageResources.Create()));
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<IoViewModel>();
        services.AddSingleton<EquipmentViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }

    public static void RegisterShellViews(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IAvaloniaViewRegistry>();

        registry.RegisterMenu(new AvaloniaMenuInfo { ViewId = "monitor", TitleResourceKey = "Shell_Tab_Monitor", Order = 10 });
        registry.RegisterMenu(new AvaloniaMenuInfo { ViewId = "io", TitleResourceKey = "Shell_Tab_IO", Order = 20 });

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo { ViewId = "monitor", TitleResourceKey = "Shell_Tab_Monitor", DockGroup = "documents" },
            typeof(MonitorView),
            typeof(MonitorViewModel),
            provider => provider.GetRequiredService<MonitorViewModel>());

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo { ViewId = "io", TitleResourceKey = "Shell_Tab_IO", DockGroup = "documents" },
            typeof(IoView),
            typeof(IoViewModel),
            provider => provider.GetRequiredService<IoViewModel>());

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo { ViewId = "equipment", TitleResourceKey = "Shell_Tool_Equipment", DockGroup = "tools", IsToolPane = true },
            typeof(EquipmentView),
            typeof(EquipmentViewModel),
            provider => provider.GetRequiredService<EquipmentViewModel>());

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo { ViewId = "log", TitleResourceKey = "Shell_Tool_Log", DockGroup = "tools", IsToolPane = true },
            typeof(LogView),
            typeof(LogViewModel),
            provider => provider.GetRequiredService<LogViewModel>());
    }
}
