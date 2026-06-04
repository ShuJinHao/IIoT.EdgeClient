using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 标准键值摘要列表，业务页面只绑定 ItemsSource，不再手写摘要行视觉结构。
/// </summary>
public class EdgeSummaryItemsControl : ItemsControl
{
    public static readonly StyledProperty<double> ItemMinWidthProperty =
        AvaloniaProperty.Register<EdgeSummaryItemsControl, double>(nameof(ItemMinWidth), 0d);

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<EdgeSummaryItemsControl, Orientation>(nameof(Orientation), Orientation.Vertical);

    public static readonly StyledProperty<IEnumerable?> SummaryItemsProperty =
        AvaloniaProperty.Register<EdgeSummaryItemsControl, IEnumerable?>(nameof(SummaryItems));

    public double ItemMinWidth
    {
        get => GetValue(ItemMinWidthProperty);
        set => SetValue(ItemMinWidthProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public IEnumerable? SummaryItems
    {
        get => GetValue(SummaryItemsProperty);
        set => SetValue(SummaryItemsProperty, value);
    }
}
