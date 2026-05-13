using IIoT.Edge.UI.Shared.PluginSystem;
using System.Windows;

namespace IIoT.Edge.UI.Shared.Modularity;

/// <summary>
/// 导航服务契约。
/// 负责维护当前视图、触发页面切换并发布导航事件。
/// </summary>
public interface INavigationService
{
    ViewModelBase? CurrentViewModel { get; }
    void NavigateTo(string viewId);
    event Action<ViewModelBase?> Navigated;
    FrameworkElement? CurrentView { get; }
}
