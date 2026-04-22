using IIoT.Edge.Module.DryRun.Presentation.ViewModels;
using IIoT.Edge.Module.DryRun.Presentation.Views;
using IIoT.Edge.Plugin.Shared.Modules;

namespace IIoT.Edge.Module.DryRun.Presentation;

public static class DryRunNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterDryRunViews(this IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(
            DryRunViewIds.Dashboard,
            typeof(DryRunDashboardPage),
            typeof(DryRunDashboardViewModel),
            cacheView: true);

        builder.RegisterMenu(new EdgeMenuInfo
        {
            Title = "DryRun",
            ViewId = DryRunViewIds.Dashboard,
            Icon = "FlaskEmptyOutline",
            Order = 30,
            RequiredPermission = string.Empty
        });

        return builder;
    }
}
