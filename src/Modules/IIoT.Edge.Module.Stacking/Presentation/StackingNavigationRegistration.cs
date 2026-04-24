using IIoT.Edge.Plugin.Shared.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Stacking.Presentation;

public static class StackingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterStackingViews(this IEdgeProcessModuleBuilder builder)
        => builder
            .RegisterStandardDataView(StackingViewIds.DataView, "叠片产品数据")
            .RegisterStandardCapacityView(StackingViewIds.CapacityView)
            .RegisterStandardIoView(StackingViewIds.IoView)
            .RegisterStandardMonitorView(StackingViewIds.Monitor)
            .RegisterStandardRecipeView(StackingViewIds.RecipeView)
            .RegisterStandardParamView(StackingViewIds.ParamView)
            .RegisterStandardHardwareConfigView(StackingViewIds.HardwareConfigView);
}
