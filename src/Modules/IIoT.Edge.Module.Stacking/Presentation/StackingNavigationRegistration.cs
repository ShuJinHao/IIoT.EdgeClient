using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.Plugin.Shared.Modules;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Presentation;

public static class StackingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterStackingViews(this IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(StackingViewIds.DataView, typeof(DataViewPage), typeof(StackingDataViewModel), cacheView: true);
        builder.RegisterRoute(StackingViewIds.CapacityView, typeof(CapacityViewPage), typeof(StackingCapacityViewModel), cacheView: true);
        builder.RegisterRoute(StackingViewIds.Monitor, typeof(MonitorViewPage), typeof(StackingMonitorViewModel), cacheView: true);
        builder.RegisterRoute(StackingViewIds.IoView, typeof(IOViewPage), typeof(StackingIoViewModel), cacheView: true);
        builder.RegisterRoute(StackingViewIds.RecipeView, typeof(RecipeViewPage), typeof(StackingRecipeViewModel), cacheView: false);
        builder.RegisterRoute(StackingViewIds.ParamView, typeof(ParamViewPage), typeof(StackingParamViewModel), cacheView: false);
        builder.RegisterRoute(StackingViewIds.HardwareConfigView, typeof(HardwareConfigPage), typeof(StackingHardwareConfigViewModel), cacheView: false);

        builder.RegisterMenu(new EdgeMenuInfo { Title = "生产数据", ViewId = StackingViewIds.DataView, Icon = "ChartBar", Order = 1, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "产能查询", ViewId = StackingViewIds.CapacityView, Icon = "ChartLine", Order = 2, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "IO 交互", ViewId = StackingViewIds.IoView, Icon = "SwapHorizontal", Order = 3, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "实时监控", ViewId = StackingViewIds.Monitor, Icon = "MonitorDashboard", Order = 4, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "产品配方", ViewId = StackingViewIds.RecipeView, Icon = "FileDocumentOutline", Order = 5, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "参数配置", ViewId = StackingViewIds.ParamView, Icon = "Cog", Order = 6, RequiredPermission = Permissions.ParamConfig });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "硬件配置", ViewId = StackingViewIds.HardwareConfigView, Icon = "ServerNetwork", Order = 7, RequiredPermission = Permissions.HardwareConfig });

        return builder;
    }
}
