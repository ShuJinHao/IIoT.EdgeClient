using Avalonia;
using Avalonia.Controls;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeTabControlVariant
{
    /// <summary>一级页签：胶囊分段样式，用于页面主导航。</summary>
    Primary,

    /// <summary>二级页签：下划线弱化样式，用于页面内部次级导航，避免与一级导航黑胶囊同权重。</summary>
    Secondary
}

/// <summary>
/// 统一页签控件，保留 Avalonia TabControl 行为，只收敛共享视觉。
/// Variant=Primary 渲染胶囊分段；Variant=Secondary 渲染下划线弱化页签。
/// 黑胶囊选中态在同一屏只允许出现一层（一级导航），页面内部页签必须使用 Secondary。
/// </summary>
public class EdgeTabControl : TabControl
{
    public static readonly StyledProperty<EdgeTabControlVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeTabControl, EdgeTabControlVariant>(nameof(Variant), EdgeTabControlVariant.Primary);

    static EdgeTabControl()
    {
        VariantProperty.Changed.AddClassHandler<EdgeTabControl>((control, _) => control.UpdateVariantClass());
    }

    public EdgeTabControl()
    {
        Classes.Add("edge-tabs");
        UpdateVariantClass();
    }

    public EdgeTabControlVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TabControl);

    private void UpdateVariantClass()
    {
        // 变体只通过 class 切换共享样式，页面不允许私写页签模板。
        if (Variant == EdgeTabControlVariant.Secondary)
        {
            if (!Classes.Contains("secondary"))
            {
                Classes.Add("secondary");
            }
        }
        else
        {
            Classes.Remove("secondary");
        }
    }
}
