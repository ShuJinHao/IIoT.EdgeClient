using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Injection.Presentation;

public static class InjectionNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterInjectionViews(this IEdgeProcessModuleBuilder builder)
        => builder
            .RegisterStandardDataView(
                InjectionViewIds.DataView,
                "注液产品数据",
                titleResourceKey: "Injection_Title_Data")
            .RegisterStandardCapacityView(InjectionViewIds.CapacityView)
            .RegisterStandardIoView(InjectionViewIds.IoView)
            .RegisterStandardMonitorView(InjectionViewIds.Monitor)
            .RegisterStandardRecipeView(InjectionViewIds.RecipeView)
            .RegisterStandardParamView(InjectionViewIds.ParamView)
            .RegisterStandardHardwareConfigView(InjectionViewIds.HardwareConfigView);
}
