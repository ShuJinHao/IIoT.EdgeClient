using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Stacking.Presentation;

public static class StackingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterStackingViews(this IEdgeProcessModuleBuilder builder)
        => builder.RegisterStandardModuleViews(
            DependencyInjection.ModuleKey,
            "叠片产品数据",
            "Stacking_Title_Data");
}
