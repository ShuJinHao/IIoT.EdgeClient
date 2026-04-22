using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Module.ScanCaptureStarter.Presentation.ViewModels;
using IIoT.Edge.Module.ScanCaptureStarter.Presentation.Views;
using IIoT.Edge.Plugin.Shared.Modules;

namespace IIoT.Edge.Module.ScanCaptureStarter.Presentation;

public static class StarterNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterScanCaptureStarterViews(this IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(StarterViewIds.Skeleton, typeof(ScanCaptureStarterSkeletonPage), typeof(StarterSkeletonViewModel), cacheView: true);
        builder.RegisterRoute(StarterViewIds.ParamView, typeof(ParamViewPage), typeof(StarterParamViewModel), cacheView: false);
        builder.RegisterRoute(StarterViewIds.HardwareConfigView, typeof(HardwareConfigPage), typeof(StarterHardwareConfigViewModel), cacheView: false);

        builder.RegisterMenu(new EdgeMenuInfo
        {
            Title = "Starter Dashboard",
            ViewId = StarterViewIds.Skeleton,
            Icon = "ViewDashboardOutline",
            Order = 30,
            RequiredPermission = string.Empty
        });
        builder.RegisterMenu(new EdgeMenuInfo
        {
            Title = "Starter Params",
            ViewId = StarterViewIds.ParamView,
            Icon = "Cog",
            Order = 31,
            RequiredPermission = Permissions.ParamConfig
        });
        builder.RegisterMenu(new EdgeMenuInfo
        {
            Title = "Starter Hardware",
            ViewId = StarterViewIds.HardwareConfigView,
            Icon = "ServerNetwork",
            Order = 32,
            RequiredPermission = Permissions.HardwareConfig
        });

        return builder;
    }
}
