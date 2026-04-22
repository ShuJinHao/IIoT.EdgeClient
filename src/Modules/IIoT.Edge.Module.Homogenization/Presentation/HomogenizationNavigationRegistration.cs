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

namespace IIoT.Edge.Module.Homogenization.Presentation;

public static class HomogenizationNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterHomogenizationViews(this IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(HomogenizationViewIds.DataView, typeof(DataViewPage), typeof(HomogenizationDataViewModel), cacheView: true);
        builder.RegisterRoute(HomogenizationViewIds.CapacityView, typeof(CapacityViewPage), typeof(HomogenizationCapacityViewModel), cacheView: true);
        builder.RegisterRoute(HomogenizationViewIds.Monitor, typeof(MonitorViewPage), typeof(HomogenizationMonitorViewModel), cacheView: true);
        builder.RegisterRoute(HomogenizationViewIds.IoView, typeof(IOViewPage), typeof(HomogenizationIoViewModel), cacheView: true);
        builder.RegisterRoute(HomogenizationViewIds.RecipeView, typeof(RecipeViewPage), typeof(HomogenizationRecipeViewModel), cacheView: false);
        builder.RegisterRoute(HomogenizationViewIds.ParamView, typeof(ParamViewPage), typeof(HomogenizationParamViewModel), cacheView: false);
        builder.RegisterRoute(HomogenizationViewIds.HardwareConfigView, typeof(HardwareConfigPage), typeof(HomogenizationHardwareConfigViewModel), cacheView: false);

        builder.RegisterMenu(new EdgeMenuInfo { Title = "生产数据", ViewId = HomogenizationViewIds.DataView, Icon = "ChartBar", Order = 1, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "产能查询", ViewId = HomogenizationViewIds.CapacityView, Icon = "ChartLine", Order = 2, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "IO 交互", ViewId = HomogenizationViewIds.IoView, Icon = "SwapHorizontal", Order = 3, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "实时监控", ViewId = HomogenizationViewIds.Monitor, Icon = "MonitorDashboard", Order = 4, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "产品配方", ViewId = HomogenizationViewIds.RecipeView, Icon = "FileDocumentOutline", Order = 5, RequiredPermission = string.Empty });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "参数配置", ViewId = HomogenizationViewIds.ParamView, Icon = "Cog", Order = 6, RequiredPermission = Permissions.ParamConfig });
        builder.RegisterMenu(new EdgeMenuInfo { Title = "硬件配置", ViewId = HomogenizationViewIds.HardwareConfigView, Icon = "ServerNetwork", Order = 7, RequiredPermission = Permissions.HardwareConfig });

        return builder;
    }
}
