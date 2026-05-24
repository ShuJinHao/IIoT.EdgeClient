using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeReportFilterBar : TemplatedControl
{
    public static readonly StyledProperty<object?> LeadingContentProperty =
        AvaloniaProperty.Register<EdgeReportFilterBar, object?>(nameof(LeadingContent));

    public static readonly StyledProperty<object?> FilterContentProperty =
        AvaloniaProperty.Register<EdgeReportFilterBar, object?>(nameof(FilterContent));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeReportFilterBar, object?>(nameof(ActionContent));

    public object? LeadingContent
    {
        get => GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public object? FilterContent
    {
        get => GetValue(FilterContentProperty);
        set => SetValue(FilterContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
