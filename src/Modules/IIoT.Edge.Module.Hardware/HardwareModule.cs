using AutoMapper;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Contracts.Auth;
using IIoT.Edge.Contracts.Model;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Hardware.HardwareConfigView;
using IIoT.Edge.Module.Hardware.IOView;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Hardware;

public class HardwareModule : IEdgeModule
{
    public string ModuleName => "Hardware";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IOViewWidget>();
        services.AddTransient<HardwareConfigWidget>();
    }

    public void ConfigureViews(IViewRegistry registry)
    {
        registry.RegisterRoute("Hardware.IOView",
            typeof(PlaceholderView), typeof(IOViewWidget));
        registry.RegisterRoute("Hardware.ConfigView",
            typeof(HardwareConfigPage), typeof(HardwareConfigWidget));

        registry.RegisterMenu(new MenuInfo
        {
            Title = "IO交互",
            WidgetId = "Hardware.IOView",
            Icon = "SwapHorizontal",
            Order = 3,
            RequiredPermission = ""
        });
        registry.RegisterMenu(new MenuInfo
        {
            Title = "硬件配置",
            WidgetId = "Hardware.ConfigView",
            Icon = "ServerNetwork",
            Order = 6,
            RequiredPermission = Permissions.HardwareConfig
        });
    }

    public IEnumerable<MenuInfo> GetMenuItems()
        => Enumerable.Empty<MenuInfo>();
}