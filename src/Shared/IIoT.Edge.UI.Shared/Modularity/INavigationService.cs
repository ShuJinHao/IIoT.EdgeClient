// 路径：src/Shared/IIoT.Edge.UI.Shared/Modularity/INavigationService.cs
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Windows;

namespace IIoT.Edge.UI.Shared.Modularity
{
    /// <summary>
    /// 导航服务契约。
    /// 只操作 ViewModel（WidgetBase），绝对不接触 View / UserControl。
    /// </summary>
    public interface INavigationService
    {
        /// <summary>当前激活的 Widget ViewModel</summary>
        WidgetBase? CurrentWidget { get; }

        /// <summary>导航到指定 WidgetId 对应的 Widget</summary>
        void NavigateTo(string widgetId);

        /// <summary>导航完成后触发，参数为新的 CurrentWidget</summary>
        event Action<WidgetBase?> Navigated;
        FrameworkElement? CurrentView { get; }
    }
}