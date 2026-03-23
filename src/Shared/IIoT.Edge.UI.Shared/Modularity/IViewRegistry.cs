// 路径：src/Shared/IIoT.Edge.UI.Shared/Modularity/IViewRegistry.cs
using IIoT.Edge.Contracts.Model;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.UI.Shared.Modularity
{
    public interface IViewRegistry
    {
        /// <summary>注册一个页面路由（WidgetId → ViewModel类型 + View类型）</summary>
        void RegisterRoute(string widgetId, Type viewType, Type viewModelType);

        /// <summary>注册菜单项</summary>
        void RegisterMenu(MenuInfo menuInfo);

        /// <summary>注册停靠面板</summary>
        void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType);

        /// <summary>根据 WidgetId 或 ContentId 查出对应的 ViewModel 类型，找不到返回 null</summary>
        Type? GetWidgetType(string widgetId);

        /// <summary>获取所有已注册的菜单项</summary>
        IReadOnlyList<MenuInfo> GetAllMenus();

        /// <summary>获取所有已注册的停靠面板</summary>
        IReadOnlyList<AnchorableInfo> GetAllAnchorables();

        Type? GetViewType(string widgetId);
    }
}