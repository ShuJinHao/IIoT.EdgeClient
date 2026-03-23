// 路径：src/Edge/IIoT.Edge.Shell/Core/ViewRegistry.cs
using IIoT.Edge.Contracts.Model;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Shell.Core
{
    public class ViewRegistry : IViewRegistry
    {
        // widgetId → ViewModel 类型
        private readonly Dictionary<string, Type> _widgetMap = new();

        // widgetId → View 类型（备用，DataTemplate 自动匹配时不需要，手动渲染时用）
        private readonly Dictionary<string, Type> _viewMap = new();

        private readonly List<MenuInfo> _menus = new();
        private readonly List<AnchorableInfo> _anchorables = new();

        public void RegisterRoute(string widgetId, Type viewType, Type viewModelType)
        {
            _widgetMap[widgetId] = viewModelType;
            _viewMap[widgetId] = viewType;
        }

        public void RegisterMenu(MenuInfo menuInfo)
        {
            _menus.Add(menuInfo);
        }

        public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType)
        {
            _anchorables.Add(info);
            _widgetMap[info.ContentId] = viewModelType;
            _viewMap[info.ContentId] = viewType;
        }

        public Type? GetWidgetType(string widgetId)
        {
            _widgetMap.TryGetValue(widgetId, out var type);
            return type;
        }

        public Type? GetViewType(string widgetId)
        {
            _viewMap.TryGetValue(widgetId, out var type);
            return type;
        }

        public IReadOnlyList<MenuInfo> GetAllMenus()
            => _menus.AsReadOnly();

        public IReadOnlyList<AnchorableInfo> GetAllAnchorables()
            => _anchorables.AsReadOnly();
    }
}