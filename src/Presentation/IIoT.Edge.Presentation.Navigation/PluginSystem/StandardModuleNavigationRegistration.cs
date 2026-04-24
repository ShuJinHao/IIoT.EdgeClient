using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Plugin.Shared.Modules;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.SharedKernel.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.PluginSystem;

/// <summary>
/// 标准模块页面注册入口。
/// 这里只组合宿主已有的通用页面，不写任何具体工序的业务逻辑。
/// </summary>
public static class StandardModuleNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterStandardDataView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title,
        int order = 1,
        string icon = "ChartBar",
        bool cacheView = true)
    {
        builder.RegisterRoute(
            viewId,
            typeof(DataViewPage),
            typeof(DataViewModel),
            sp => ActivatorUtilities.CreateInstance<DataViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu(title, viewId, icon, order));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardCapacityView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "产能查询",
        int order = 2,
        string icon = "ChartLine",
        bool cacheView = true)
    {
        builder.RegisterRoute(
            viewId,
            typeof(CapacityViewPage),
            typeof(CapacityViewModel),
            sp => ActivatorUtilities.CreateInstance<CapacityViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu("产能", viewId, icon, order));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardIoView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "IO 交互",
        int order = 3,
        string icon = "SwapHorizontal",
        bool cacheView = true)
    {
        builder.RegisterRoute(
            viewId,
            typeof(IOViewPage),
            typeof(IoViewViewModel),
            sp => ActivatorUtilities.CreateInstance<IoViewViewModel>(sp, viewId, title, builder.ModuleId),
            cacheView);
        builder.RegisterMenu(CreateMenu("IO交互", viewId, icon, order));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardMonitorView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "实时监控",
        int order = 4,
        string icon = "MonitorDashboard",
        bool cacheView = true)
    {
        builder.RegisterRoute(
            viewId,
            typeof(MonitorViewPage),
            typeof(MonitorViewModel),
            sp => ActivatorUtilities.CreateInstance<MonitorViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu("监控", viewId, icon, order));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardRecipeView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "产品配方",
        int order = 5,
        string icon = "FileDocumentOutline",
        bool cacheView = false)
    {
        builder.RegisterRoute(
            viewId,
            typeof(RecipeViewPage),
            typeof(RecipeViewModel),
            sp => ActivatorUtilities.CreateInstance<RecipeViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu("配方", viewId, icon, order));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardParamView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "参数配置",
        int order = 6,
        string icon = "Cog",
        bool cacheView = false)
    {
        builder.RegisterRoute(
            viewId,
            typeof(ParamViewPage),
            typeof(ParamViewModel),
            sp => ActivatorUtilities.CreateInstance<ParamViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu("参数配置", viewId, icon, order, Permissions.ParamConfig));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardHardwareConfigView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string title = "硬件配置",
        int order = 7,
        string icon = "ServerNetwork",
        bool cacheView = false)
    {
        builder.RegisterRoute(
            viewId,
            typeof(HardwareConfigPage),
            typeof(HardwareConfigViewModel),
            sp => ActivatorUtilities.CreateInstance<HardwareConfigViewModel>(sp, viewId, title),
            cacheView);
        builder.RegisterMenu(CreateMenu("硬件配置", viewId, icon, order, Permissions.HardwareConfig));
        return builder;
    }

    private static EdgeMenuInfo CreateMenu(
        string title,
        string viewId,
        string icon,
        int order,
        string requiredPermission = "")
        => new()
        {
            Title = title,
            ViewId = viewId,
            Icon = icon,
            Order = order,
            RequiredPermission = requiredPermission
        };
}
