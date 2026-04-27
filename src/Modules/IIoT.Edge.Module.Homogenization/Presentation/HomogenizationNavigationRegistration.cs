using IIoT.Edge.Module.Homogenization.Presentation.Views;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public static class HomogenizationNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterHomogenizationViews(this IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(
            HomogenizationViewIds.DataView,
            typeof(HomogenizationDataPage),
            typeof(HomogenizationDataViewModel),
            cacheView: true);

        builder.RegisterMenu(new EdgeMenuInfo
        {
            Title = "数据",
            TitleResourceKey = "Homogenization_Menu_Data",
            ViewId = HomogenizationViewIds.DataView,
            Icon = "ChartBar",
            Order = 1,
            RequiredPermission = string.Empty
        });

        return builder
            .RegisterStandardCapacityView(HomogenizationViewIds.CapacityView)
            .RegisterStandardIoView(HomogenizationViewIds.IoView)
            .RegisterStandardMonitorView(HomogenizationViewIds.Monitor)
            .RegisterStandardRecipeView(HomogenizationViewIds.RecipeView)
            .RegisterStandardParamView(HomogenizationViewIds.ParamView)
            .RegisterStandardHardwareConfigView(HomogenizationViewIds.HardwareConfigView);
    }
}
