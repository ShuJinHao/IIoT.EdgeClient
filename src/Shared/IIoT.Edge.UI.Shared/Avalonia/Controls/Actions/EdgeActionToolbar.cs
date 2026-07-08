using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 页面级操作按钮组容器，统一配置类页面工具栏排列。
/// </summary>
public class EdgeActionToolbar : StackPanel
{
    public EdgeActionToolbar()
    {
        Orientation = global::Avalonia.Layout.Orientation.Horizontal;
        Spacing = 8;
        Classes.Add("edge-action-toolbar");
    }
}
