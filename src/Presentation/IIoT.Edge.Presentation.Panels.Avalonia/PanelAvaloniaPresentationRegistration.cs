using IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Panels.Avalonia.Views;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Avalonia;

public static class PanelAvaloniaPresentationRegistration
{
    public static void RegisterPanelViews(IAvaloniaViewRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.RegisterMenu(new AvaloniaMenuInfo
        {
            ViewId = "Core.SysLog",
            Title = "运行日志",
            TitleResourceKey = "Panels_Title_RuntimeLog",
            Icon = "FileDocumentOutline",
            Order = 1
        });

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = "Core.SysLog",
                TitleResourceKey = "Panels_Title_RuntimeLog",
                DockGroup = "tools",
                IsToolPane = true
            },
            typeof(LogView),
            typeof(LogViewModel),
            provider => provider.GetRequiredService<LogViewModel>());

        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = "Core.Equipment",
                TitleResourceKey = "Panels_Title_DeviceStatus",
                DockGroup = "tools",
                IsToolPane = true
            },
            typeof(EquipmentView),
            typeof(EquipmentViewModel),
            provider => provider.GetRequiredService<EquipmentViewModel>());
    }
}
