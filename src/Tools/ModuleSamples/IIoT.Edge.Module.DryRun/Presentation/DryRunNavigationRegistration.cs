using IIoT.Edge.Module.DryRun.Presentation.ViewModels;
using IIoT.Edge.Module.DryRun.Presentation.Views;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Module.DryRun.Presentation;

public static class DryRunNavigationRegistration
{
    public static IViewRegistry RegisterDryRunViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(
            DryRunViewIds.Dashboard,
            typeof(DryRunDashboardPage),
            typeof(DryRunDashboardViewModel),
            cacheView: true);

        registry.RegisterMenu(new MenuInfo
        {
            Title = "DryRun",
            ViewId = DryRunViewIds.Dashboard,
            Icon = "FlaskEmptyOutline",
            Order = 30,
            RequiredPermission = string.Empty
        });

        return registry;
    }
}
