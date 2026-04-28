using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Module.Homogenization.Presentation.Views;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public static class HomogenizationNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterHomogenizationViews(this IEdgeProcessModuleBuilder builder)
    {
        var viewIds = StandardModuleViewIds.Create(DependencyInjection.ModuleKey);
        builder.RegisterRoute(
            viewIds.DataView,
            typeof(HomogenizationDataPage),
            typeof(HomogenizationDataViewModel),
            cacheView: true);

        builder.RegisterMenu(new ModuleMenuDescriptor
        {
            Title = "数据",
            TitleResourceKey = "Homogenization_Menu_Data",
            ViewId = viewIds.DataView,
            Icon = "ChartBar",
            Order = 1,
            RequiredPermission = string.Empty
        });

        return builder
            .RegisterStandardCapacityView(viewIds.CapacityView)
            .RegisterStandardIoView(viewIds.IoView)
            .RegisterStandardMonitorView(viewIds.Monitor)
            .RegisterStandardRecipeView(viewIds.RecipeView)
            .RegisterStandardParamView(viewIds.ParamView)
            .RegisterStandardHardwareConfigView(viewIds.HardwareConfigView);
    }
}
