using Avalonia.Controls;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.HardwareConfig.Views;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.Presentation.Navigation.Avalonia.Views;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Avalonia;

public static class NavigationAvaloniaPresentationRegistration
{
    public static void RegisterNavigationViews(IAvaloniaViewRegistry registry, IReadOnlyCollection<string> moduleIds)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(moduleIds);

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
            Title = "系统诊断",
            TitleResourceKey = "Navigation_Menu_CoreDiagnostics",
            Icon = "Stethoscope",
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
        RegisterDocument<MonitorViewPage, MonitorViewModel>(registry, ids.Monitor, "Navigation_Menu_Monitor", 4, "监控", "MonitorDashboard");
        RegisterDocument<CapacityViewPage, CapacityViewModel>(registry, ids.CapacityView, "Navigation_Menu_Capacity", 2, "产能", "ChartLine");
        RegisterDocument<IOViewPage, IoViewViewModel>(registry, ids.IoView, "Navigation_Menu_Io", 3, "IO 交互", "SwapHorizontal");
        RegisterDocument<RecipeViewPage, RecipeViewModel>(registry, ids.RecipeView, "Navigation_Menu_Recipe", 5, "产品配方", "FileDocumentOutline");
        RegisterDocument<ParamViewPage, ParamViewModel>(registry, ids.ParamView, "Navigation_Menu_ParamConfig", 6, "参数配置", "Cog", Permissions.ParamConfig);
        RegisterDocument<HardwareConfigPage, HardwareConfigViewModel>(registry, ids.HardwareConfigView, "Navigation_Menu_HardwareConfig", 7, "硬件配置", "ServerNetwork", Permissions.HardwareConfig);
        RegisterDocument<PlcTaskBindingPage, PlcTaskBindingViewModel>(registry, ids.PlcTaskBindingView, "Navigation_Menu_PlcTaskBinding", 8, "PLC 任务绑定", "Tune", Permissions.HardwareConfig);
    }

    private static void RegisterDocument<TView, TViewModel>(
        IAvaloniaViewRegistry registry,
        string viewId,
        string titleResourceKey,
        int order,
        string titleFallback,
        string icon,
        string requiredPermission = "")
        where TView : Control
        where TViewModel : NavigationPageViewModelBase
    {
        registry.RegisterMenu(new AvaloniaMenuInfo
        {
            ViewId = viewId,
            Title = titleFallback,
            TitleResourceKey = titleResourceKey,
            Icon = icon,
            Order = order,
            RequiredPermission = requiredPermission
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
