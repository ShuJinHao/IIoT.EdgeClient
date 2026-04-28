using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Stacking.Presentation;

public static class StackingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterStackingViews(this IEdgeProcessModuleBuilder builder)
    {
        var viewIds = StandardModuleViewIds.Create(DependencyInjection.ModuleKey);
        return builder
            .RegisterStandardDataView(
                viewIds.DataView,
                "叠片产品数据",
                titleResourceKey: "Stacking_Title_Data")
            .RegisterStandardCapacityView(viewIds.CapacityView)
            .RegisterStandardIoView(viewIds.IoView)
            .RegisterStandardMonitorView(viewIds.Monitor)
            .RegisterStandardRecipeView(viewIds.RecipeView)
            .RegisterStandardParamView(viewIds.ParamView)
            .RegisterStandardHardwareConfigView(viewIds.HardwareConfigView);
    }
}
