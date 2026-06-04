using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 多段状态条，用于上传健康度、任务区间等连续状态概览。
/// </summary>
public class EdgeStatusSegmentBar : ItemsControl
{
    public static readonly StyledProperty<double> SegmentWidthProperty =
        AvaloniaProperty.Register<EdgeStatusSegmentBar, double>(nameof(SegmentWidth), 28d);

    public static readonly StyledProperty<double> SegmentHeightProperty =
        AvaloniaProperty.Register<EdgeStatusSegmentBar, double>(nameof(SegmentHeight), 8d);

    public static readonly StyledProperty<double> SegmentSpacingProperty =
        AvaloniaProperty.Register<EdgeStatusSegmentBar, double>(nameof(SegmentSpacing), 0d);

    public double SegmentWidth
    {
        get => GetValue(SegmentWidthProperty);
        set => SetValue(SegmentWidthProperty, value);
    }

    public double SegmentHeight
    {
        get => GetValue(SegmentHeightProperty);
        set => SetValue(SegmentHeightProperty, value);
    }

    public double SegmentSpacing
    {
        get => GetValue(SegmentSpacingProperty);
        set => SetValue(SegmentSpacingProperty, value);
    }
}
