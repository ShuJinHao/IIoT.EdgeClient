using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;
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
    public static IEdgeProcessModuleBuilder RegisterStandardModuleViews(
        this IEdgeProcessModuleBuilder builder,
        string moduleId,
        string dataViewTitle,
        string dataViewTitleResourceKey,
        Type? customDataViewType = null,
        Type? customDataViewModelType = null,
        string? dataMenuTitle = null,
        string? dataMenuTitleResourceKey = null,
        bool cacheDataView = true)
        => builder.RegisterStandardModuleViews(
            moduleId,
            dataViewTitle,
            dataViewTitleResourceKey,
            customDataViewType,
            customDataViewModelType,
            dataMenuTitle,
            dataMenuTitleResourceKey,
            cacheDataView,
            supportsRecipe: true);

    public static IEdgeProcessModuleBuilder RegisterStandardModuleViews(
        this IEdgeProcessModuleBuilder builder,
        string moduleId,
        string dataViewTitle,
        string dataViewTitleResourceKey,
        bool supportsRecipe)
        => builder.RegisterStandardModuleViews(
            moduleId,
            dataViewTitle,
            dataViewTitleResourceKey,
            customDataViewType: null,
            customDataViewModelType: null,
            dataMenuTitle: null,
            dataMenuTitleResourceKey: null,
            cacheDataView: true,
            supportsRecipe);

    public static IEdgeProcessModuleBuilder RegisterStandardModuleViews(
        this IEdgeProcessModuleBuilder builder,
        string moduleId,
        string dataViewTitle,
        string dataViewTitleResourceKey,
        Type? customDataViewType,
        Type? customDataViewModelType,
        string? dataMenuTitle,
        string? dataMenuTitleResourceKey,
        bool cacheDataView,
        bool supportsRecipe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataViewTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataViewTitleResourceKey);

        if ((customDataViewType is null) != (customDataViewModelType is null))
        {
            throw new ArgumentException("自定义数据页必须同时提供 View 和 ViewModel 类型。");
        }

        var viewIds = StandardModuleViewIds.Create(moduleId);

        if (customDataViewType is null || customDataViewModelType is null)
        {
            builder.RegisterStandardDataView(
                viewIds.DataView,
                dataViewTitle,
                cacheView: cacheDataView,
                titleResourceKey: dataViewTitleResourceKey);
        }
        else
        {
            builder.RegisterRoute(
                viewIds.DataView,
                customDataViewType,
                customDataViewModelType,
                cacheDataView);
            builder.RegisterMenu(CreateMenu(
                dataMenuTitle ?? dataViewTitle,
                viewIds.DataView,
                "ChartBar",
                1,
                titleResourceKey: dataMenuTitleResourceKey ?? dataViewTitleResourceKey));
        }

        var standardBuilder = builder
            .RegisterStandardCapacityView(viewIds.CapacityView)
            .RegisterStandardIoView(viewIds.IoView)
            .RegisterStandardMonitorView(viewIds.Monitor);

        if (supportsRecipe)
        {
            standardBuilder.RegisterStandardRecipeView(viewIds.RecipeView);
        }

        return standardBuilder
            .RegisterStandardParamView(viewIds.ParamView)
            .RegisterStandardHardwareConfigView(viewIds.HardwareConfigView)
            .RegisterStandardPlcTaskBindingView(viewIds.PlcTaskBindingView);
    }

    public static IEdgeProcessModuleBuilder RegisterStandardDataView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 1,
        string icon = "ChartBar",
        bool cacheView = true,
        string titleResourceKey = "Navigation_Menu_Data")
    {
        title ??= "生产数据";
        builder.RegisterRoute(
            viewId,
            typeof(DataViewPage),
            typeof(DataViewModel),
            sp => ActivatorUtilities.CreateInstance<DataViewModel>(sp, viewId, titleResourceKey, title),
            cacheView);
        builder.RegisterMenu(CreateMenu(title, viewId, icon, order, titleResourceKey: titleResourceKey));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardCapacityView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 2,
        string icon = "ChartLine",
        bool cacheView = true)
    {
        title ??= "产能查询";
        builder.RegisterRoute(
            viewId,
            typeof(CapacityViewPage),
            typeof(CapacityViewModel),
            sp => ActivatorUtilities.CreateInstance<CapacityViewModel>(sp, viewId, "Navigation_Title_CapacityQuery", title),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "产能",
            viewId,
            icon,
            order,
            titleResourceKey: "Navigation_Menu_Capacity"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardIoView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 3,
        string icon = "SwapHorizontal",
        bool cacheView = true)
    {
        title ??= "IO 交互";
        builder.RegisterRoute(
            viewId,
            typeof(IOViewPage),
            typeof(IoViewViewModel),
            sp => ActivatorUtilities.CreateInstance<IoViewViewModel>(sp, viewId, "Navigation_Title_IoInteract", title, builder.ModuleId),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "IO交互",
            viewId,
            icon,
            order,
            titleResourceKey: "Navigation_Menu_Io"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardMonitorView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 4,
        string icon = "MonitorDashboard",
        bool cacheView = true)
    {
        title ??= "实时监控";
        builder.RegisterRoute(
            viewId,
            typeof(MonitorViewPage),
            typeof(MonitorViewModel),
            sp => ActivatorUtilities.CreateInstance<MonitorViewModel>(sp, viewId, "Navigation_Title_RealtimeMonitor", title),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "监控",
            viewId,
            icon,
            order,
            titleResourceKey: "Navigation_Menu_Monitor"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardRecipeView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 5,
        string icon = "FileDocumentOutline",
        bool cacheView = false)
    {
        title ??= "产品配方";
        builder.RegisterRoute(
            viewId,
            typeof(RecipeViewPage),
            typeof(RecipeViewModel),
            sp => ActivatorUtilities.CreateInstance<RecipeViewModel>(sp, viewId, "Navigation_Title_ProductRecipe", title),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "配方",
            viewId,
            icon,
            order,
            titleResourceKey: "Navigation_Menu_Recipe"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardParamView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 6,
        string icon = "Cog",
        bool cacheView = false)
    {
        title ??= "参数配置";
        builder.RegisterRoute(
            viewId,
            typeof(ParamViewPage),
            typeof(ParamViewModel),
            sp => ActivatorUtilities.CreateInstance<ParamViewModel>(sp, viewId, "Navigation_Title_ParamConfig", title),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "参数配置",
            viewId,
            icon,
            order,
            Permissions.ParamConfig,
            "Navigation_Menu_ParamConfig"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardHardwareConfigView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 7,
        string icon = "ServerNetwork",
        bool cacheView = false)
    {
        title ??= "硬件配置";
        builder.RegisterRoute(
            viewId,
            typeof(HardwareConfigPage),
            typeof(HardwareConfigViewModel),
            sp => ActivatorUtilities.CreateInstance<HardwareConfigViewModel>(sp, viewId, "Navigation_Title_HardwareConfig", title),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "硬件配置",
            viewId,
            icon,
            order,
            Permissions.HardwareConfig,
            "Navigation_Menu_HardwareConfig"));
        return builder;
    }

    public static IEdgeProcessModuleBuilder RegisterStandardPlcTaskBindingView(
        this IEdgeProcessModuleBuilder builder,
        string viewId,
        string? title = null,
        int order = 8,
        string icon = "Tune",
        bool cacheView = false)
    {
        title ??= "任务绑定";
        builder.RegisterRoute(
            viewId,
            typeof(PlcTaskBindingPage),
            typeof(PlcTaskBindingViewModel),
            sp => ActivatorUtilities.CreateInstance<PlcTaskBindingViewModel>(
                sp,
                viewId,
                "Navigation_Title_PlcTaskBinding",
                title,
                builder.ModuleId),
            cacheView);
        builder.RegisterMenu(CreateMenu(
            "任务绑定",
            viewId,
            icon,
            order,
            Permissions.HardwareConfig,
            "Navigation_Menu_PlcTaskBinding"));
        return builder;
    }

    private static ModuleMenuDescriptor CreateMenu(
        string title,
        string viewId,
        string icon,
        int order,
        string requiredPermission = "",
        string titleResourceKey = "")
        => new()
        {
            Title = title,
            TitleResourceKey = titleResourceKey,
            ViewId = viewId,
            Icon = icon,
            Order = order,
            RequiredPermission = requiredPermission
        };
}
