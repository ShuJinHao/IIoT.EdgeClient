using IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Panels.Avalonia.Views;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Avalonia;

public static class PanelAvaloniaPresentationRegistration
{
    public static void RegisterPanelViews(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IAvaloniaViewRegistry>();

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = "Core.Equipment",
                TitleResourceKey = "Shell_EquipmentInfo",
                DockGroup = "tools",
                IsToolPane = true
            },
            typeof(EquipmentView),
            typeof(EquipmentViewModel),
            provider => provider.GetRequiredService<EquipmentViewModel>());

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = "Core.SysLog",
                TitleResourceKey = "Shell_SystemLog",
                DockGroup = "tools",
                IsToolPane = true
            },
            typeof(LogView),
            typeof(LogViewModel),
            provider => provider.GetRequiredService<LogViewModel>());
    }
}
