using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Module.Injection.Presentation;

public static class InjectionNavigationRegistration
{
    public static IViewRegistry RegisterInjectionViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(InjectionViewIds.DataView, typeof(DataViewPage), typeof(InjectionDataViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.CapacityView, typeof(CapacityViewPage), typeof(InjectionCapacityViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.Monitor, typeof(MonitorViewPage), typeof(InjectionMonitorViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.IoView, typeof(IOViewPage), typeof(InjectionIoViewModel), cacheView: true);
        registry.RegisterRoute(InjectionViewIds.RecipeView, typeof(RecipeViewPage), typeof(InjectionRecipeViewModel), cacheView: false);
        registry.RegisterRoute(InjectionViewIds.ParamView, typeof(ParamViewPage), typeof(InjectionParamViewModel), cacheView: false);
        registry.RegisterRoute(InjectionViewIds.HardwareConfigView, typeof(HardwareConfigPage), typeof(InjectionHardwareConfigViewModel), cacheView: false);

        registry.RegisterMenu(new MenuInfo { Title = "生产数据", ViewId = InjectionViewIds.DataView, Icon = "ChartBar", Order = 1, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "产能查询", ViewId = InjectionViewIds.CapacityView, Icon = "ChartLine", Order = 2, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "IO 交互", ViewId = InjectionViewIds.IoView, Icon = "SwapHorizontal", Order = 3, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "实时监控", ViewId = InjectionViewIds.Monitor, Icon = "MonitorDashboard", Order = 4, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "产品配方", ViewId = InjectionViewIds.RecipeView, Icon = "FileDocumentOutline", Order = 5, RequiredPermission = string.Empty });
        registry.RegisterMenu(new MenuInfo { Title = "参数配置", ViewId = InjectionViewIds.ParamView, Icon = "Cog", Order = 6, RequiredPermission = Permissions.ParamConfig });
        registry.RegisterMenu(new MenuInfo { Title = "硬件配置", ViewId = InjectionViewIds.HardwareConfigView, Icon = "ServerNetwork", Order = 7, RequiredPermission = Permissions.HardwareConfig });

        return registry;
    }
}
