// 路径：src/Modules/IIoT.Edge.Module.Production/ProductionModule.cs
using IIoT.Edge.Contracts.Model;
using IIoT.Edge.Module.Production.CapacityView;
using IIoT.Edge.Module.Production.DataView;
using IIoT.Edge.Module.Production.Equipment;
using IIoT.Edge.Module.Production.Monitor;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Production
{
    public class ProductionModule : IEdgeModule
    {
        public string ModuleName => "Production";

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void ConfigureViews(IViewRegistry registry)
        {
            registry.RegisterRoute("Production.DataView",
                typeof(DataViewPage), typeof(DataViewWidget));
            registry.RegisterRoute("Production.CapacityView",
                typeof(CapacityViewPage), typeof(CapacityViewWidget));

            registry.RegisterAnchorable(
                new AnchorableInfo
                {
                    Title = "设备信息",
                    ContentId = "Core.Equipment",
                    InitialPosition = AnchorablePosition.Right,
                    IsVisible = true
                },
                typeof(EquipmentView),
                typeof(EquipmentWidget));

            // 实时数据监控面板
            registry.RegisterAnchorable(
                new AnchorableInfo
                {
                    Title = "实时数据监控",
                    ContentId = "Core.Monitor",
                    InitialPosition = AnchorablePosition.Right,
                    IsVisible = true
                },
                typeof(MonitorView),
                typeof(MonitorWidget));

            registry.RegisterMenu(new MenuInfo
            {
                Title = "生产数据",
                WidgetId = "Production.DataView",
                Icon = "ChartBar",
                Order = 1,
                RequiredPermission = ""
            });
            registry.RegisterMenu(new MenuInfo
            {
                Title = "产能查询",
                WidgetId = "Production.CapacityView",
                Icon = "ChartLine",
                Order = 2,
                RequiredPermission = ""
            });
        }

        public IEnumerable<MenuInfo> GetMenuItems()
            => Enumerable.Empty<MenuInfo>();
    }
}