using Avalonia;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一状态圆点，用语义状态 token 表达在线、离线、告警等状态。
/// </summary>
public class EdgeStatusDot : EdgeStatusControlBase
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<EdgeStatusDot, double>(nameof(Size), 8);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<EdgeStatusDot, IBrush?>(nameof(Fill));

    static EdgeStatusDot()
    {
        StatusProperty.OverrideDefaultValue<EdgeStatusDot>(EdgeVisualStatus.Info);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }
}
