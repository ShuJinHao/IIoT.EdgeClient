namespace IIoT.Edge.UI.Shared.PluginSystem;

/// <summary>
/// 页面视图模型统一契约。
/// 定义视图标识、布局信息以及激活生命周期入口。
/// </summary>
public interface IViewModelContract
{
    string ViewId { get; }
    string ViewTitle { get; }

    int LayoutRow { get; set; }
    int LayoutColumn { get; set; }
    int RowSpan { get; set; }
    int ColumnSpan { get; set; }
    bool IsVisible { get; set; }

    Task OnActivatedAsync();
    Task OnDeactivatedAsync();
}
