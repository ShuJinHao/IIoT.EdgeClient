using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Injection.Presentation;

public static class InjectionNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterInjectionViews(this IEdgeProcessModuleBuilder builder)
        => builder.RegisterStandardModuleViews(
            DependencyInjection.ModuleKey,
            "注液产品数据",
            "Injection_Title_Data");
}
