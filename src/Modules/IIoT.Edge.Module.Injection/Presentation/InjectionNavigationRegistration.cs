using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Injection.Presentation;

public static class InjectionNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterInjectionViews(this IEdgeProcessModuleBuilder builder)
    {
        var viewIds = StandardModuleViewIds.Create(DependencyInjection.ModuleKey);
        return builder
            .RegisterStandardDataView(
                viewIds.DataView,
                "注液产品数据",
                titleResourceKey: "Injection_Title_Data")
            .RegisterStandardCapacityView(viewIds.CapacityView)
            .RegisterStandardIoView(viewIds.IoView)
            .RegisterStandardMonitorView(viewIds.Monitor)
            .RegisterStandardRecipeView(viewIds.RecipeView)
            .RegisterStandardParamView(viewIds.ParamView)
            .RegisterStandardHardwareConfigView(viewIds.HardwareConfigView);
    }
}
