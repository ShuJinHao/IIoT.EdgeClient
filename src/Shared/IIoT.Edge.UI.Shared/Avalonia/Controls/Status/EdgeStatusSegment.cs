using Avalonia;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 状态分段条中的单个状态片段，用 EdgeVisualStatus 表达颜色语义。
/// </summary>
public class EdgeStatusSegment : EdgeStatusControlBase
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<EdgeStatusSegment, string?>(nameof(Label));

    public static readonly StyledProperty<double> SegmentWidthProperty =
        AvaloniaProperty.Register<EdgeStatusSegment, double>(nameof(SegmentWidth), 28d);

    public static readonly StyledProperty<double> SegmentHeightProperty =
        AvaloniaProperty.Register<EdgeStatusSegment, double>(nameof(SegmentHeight), 8d);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

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
}
