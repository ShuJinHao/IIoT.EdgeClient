using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeMetricStrip : ItemsControl
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<EdgeMetricStrip, double>(nameof(MinItemWidth), 220);

    static EdgeMetricStrip()
    {
        MinItemWidthProperty.Changed.AddClassHandler<EdgeMetricStrip>((strip, _) => strip.InvalidateMeasure());
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        container.MinWidth = MinItemWidth;
        container.Margin = new Thickness(0, 0, 12, 12);
    }
}
