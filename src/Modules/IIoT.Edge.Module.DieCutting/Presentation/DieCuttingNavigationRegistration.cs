using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.DieCutting.Presentation;

public static class DieCuttingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterDieCuttingViews(this IEdgeProcessModuleBuilder builder)
        => builder.RegisterStandardModuleViews(
            DependencyInjection.ModuleKey,
            "模切采样",
            "DieCutting_Menu_Data",
            supportsRecipe: false);
}
