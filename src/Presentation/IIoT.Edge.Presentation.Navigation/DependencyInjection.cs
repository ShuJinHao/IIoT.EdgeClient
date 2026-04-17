using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.DryRun.DashboardView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Features.Stacking.SkeletonView;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation;

public static class DependencyInjection
{
    public static IServiceCollection AddNavigationPresentation(this IServiceCollection services)
    {
        services.AddSingleton<ParamViewModel>();
        services.AddSingleton<IoViewViewModel>();
        services.AddSingleton<HardwareConfigViewModel>();
        services.AddSingleton<RecipeViewModel>();
        services.AddSingleton<CapacityViewModel>();
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<DataViewModel>();
        services.AddSingleton<StackingSkeletonViewModel>();
        services.AddSingleton<DryRunDashboardViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();

        services.AddTransient<ParamViewPage>();
        services.AddTransient<IOViewPage>();
        services.AddTransient<HardwareConfigPage>();
        services.AddTransient<RecipeViewPage>();
        services.AddTransient<CapacityViewPage>();
        services.AddTransient<MonitorViewPage>();
        services.AddTransient<DataViewPage>();
        services.AddTransient<StackingSkeletonPage>();
        services.AddTransient<DryRunDashboardPage>();
        services.AddTransient<DiagnosticsPage>();

        return services;
    }

    public static IViewRegistry RegisterInjectionViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(InjectionViewIds.DataView, typeof(DataViewPage), typeof(DataViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.CapacityView, typeof(CapacityViewPage), typeof(CapacityViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.Monitor, typeof(MonitorViewPage), typeof(MonitorViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.IoView, typeof(IOViewPage), typeof(IoViewViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.RecipeView, typeof(RecipeViewPage), typeof(RecipeViewModel), cacheView: false);
        registry.RegisterRoute(InjectionViewIds.ParamView, typeof(ParamViewPage), typeof(ParamViewModel), cacheView: false);
        registry.RegisterRoute(InjectionViewIds.HardwareConfigView, typeof(HardwareConfigPage), typeof(HardwareConfigViewModel), cacheView: false);

        registry.RegisterMenu(new MenuInfo { Title = "生产数据", ViewId = InjectionViewIds.DataView, Icon = "ChartBar", Order = 1, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "产能查询", ViewId = InjectionViewIds.CapacityView, Icon = "ChartLine", Order = 2, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "IO 交互", ViewId = InjectionViewIds.IoView, Icon = "SwapHorizontal", Order = 3, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "实时监控", ViewId = InjectionViewIds.Monitor, Icon = "MonitorDashboard", Order = 4, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "产品配方", ViewId = InjectionViewIds.RecipeView, Icon = "FileDocumentOutline", Order = 5, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "参数配置", ViewId = InjectionViewIds.ParamView, Icon = "Cog", Order = 6, RequiredPermission = Permissions.ParamConfig });
        registry.RegisterMenu(new MenuInfo { Title = "硬件配置", ViewId = InjectionViewIds.HardwareConfigView, Icon = "ServerNetwork", Order = 7, RequiredPermission = Permissions.HardwareConfig });

        return registry;
    }

    public static IViewRegistry RegisterStackingViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(
            StackingViewIds.PlaceholderDashboard,
            typeof(StackingSkeletonPage),
            typeof(StackingSkeletonViewModel),
            cacheView: false);
        registry.RegisterMenu(new MenuInfo
        {
            Title = "Stacking Skeleton",
            ViewId = StackingViewIds.PlaceholderDashboard,
            Icon = "Cog",
            Order = 80,
            RequiredPermission = string.Empty
        });

        return registry;
    }

    public static IViewRegistry RegisterDryRunViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(
            DryRunViewIds.Dashboard,
            typeof(DryRunDashboardPage),
            typeof(DryRunDashboardViewModel),
            cacheView: false);
        registry.RegisterMenu(new MenuInfo
        {
            Title = "DryRun Dashboard",
            ViewId = DryRunViewIds.Dashboard,
            Icon = "FlaskOutline",
            Order = 90,
            RequiredPermission = string.Empty
        });

        return registry;
    }
}
