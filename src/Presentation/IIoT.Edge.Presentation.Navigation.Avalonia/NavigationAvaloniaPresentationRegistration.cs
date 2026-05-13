using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Views;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Avalonia;

public static class NavigationAvaloniaPresentationRegistration
{
    public static void RegisterNavigationViews(IServiceProvider services, IReadOnlyCollection<string> moduleIds)
    {
        var registry = services.GetRequiredService<IAvaloniaViewRegistry>();
        RegisterDiagnostics(registry);

        var modules = moduleIds.Count == 0
            ? ["Homogenization"]
            : moduleIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var moduleId in modules)
        {
            RegisterModule(registry, moduleId);
        }
    }

    private static void RegisterDiagnostics(IAvaloniaViewRegistry registry)
    {
        registry.RegisterMenu(new AvaloniaMenuInfo
        {
            ViewId = CoreAvaloniaViewIds.Diagnostics,
            TitleResourceKey = "Navigation_Menu_CoreDiagnostics",
            Order = 999
        });
        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = CoreAvaloniaViewIds.Diagnostics,
                TitleResourceKey = "Navigation_Menu_CoreDiagnostics",
                DockGroup = "documents"
            },
            typeof(DiagnosticsPage),
            typeof(DiagnosticsViewModel),
            provider => ActivatorUtilities.CreateInstance<DiagnosticsViewModel>(
                provider,
                CoreAvaloniaViewIds.Diagnostics,
                "Navigation_Menu_CoreDiagnostics",
                "系统诊断"),
            cacheView: false);
    }

    private static void RegisterModule(IAvaloniaViewRegistry registry, string moduleId)
    {
        var ids = StandardAvaloniaModuleViewIds.Create(moduleId);
        RegisterDocument<MonitorViewPage, MonitorViewModel>(registry, ids.Monitor, "Navigation_Menu_Monitor", 4, "实时监控");
        RegisterDocument<DataViewPage, DataViewModel>(registry, ids.DataView, "Navigation_Menu_Data", 1, "生产数据");
        RegisterDocument<CapacityViewPage, CapacityViewModel>(registry, ids.CapacityView, "Navigation_Menu_Capacity", 2, "产能");
        RegisterDocument<PlcTaskBindingPage, PlcTaskBindingViewModel>(registry, ids.PlcTaskBindingView, "Navigation_Menu_PlcTaskBinding", 8, "PLC 任务绑定");
    }

    private static void RegisterDocument<TView, TViewModel>(
        IAvaloniaViewRegistry registry,
        string viewId,
        string titleResourceKey,
        int order,
        string titleFallback)
        where TView : Control
        where TViewModel : NavigationPageViewModelBase
    {
        registry.RegisterMenu(new AvaloniaMenuInfo
        {
            ViewId = viewId,
            TitleResourceKey = titleResourceKey,
            Order = order
        });
        registry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = viewId,
                TitleResourceKey = titleResourceKey,
                DockGroup = "documents"
            },
            typeof(TView),
            typeof(TViewModel),
            provider => ActivatorUtilities.CreateInstance<TViewModel>(provider, viewId, titleResourceKey, titleFallback));
    }
}
