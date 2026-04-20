using IIoT.Edge.Module.Stacking.Presentation.ViewModels;
using IIoT.Edge.Module.Stacking.Presentation.Views;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Module.Stacking.Presentation;

public static class StackingNavigationRegistration
{
    public static IViewRegistry RegisterStackingViews(this IViewRegistry registry)
    {
        registry.RegisterRoute(
            StackingViewIds.PlaceholderDashboard,
            typeof(StackingSkeletonPage),
            typeof(StackingSkeletonViewModel),
            cacheView: true);

        registry.RegisterMenu(new MenuInfo
        {
            Title = "Stacking",
            ViewId = StackingViewIds.PlaceholderDashboard,
            Icon = "LayersTripleOutline",
            Order = 20,
            RequiredPermission = string.Empty
        });

        return registry;
    }
}
