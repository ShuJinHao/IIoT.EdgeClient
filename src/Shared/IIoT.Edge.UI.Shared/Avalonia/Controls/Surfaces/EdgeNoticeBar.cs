using Avalonia;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeNoticeBar : EdgeStatusControlBase
{
    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(Icon));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(Content));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(ActionContent));

    static EdgeNoticeBar()
    {
        StatusProperty.OverrideDefaultValue<EdgeNoticeBar>(EdgeVisualStatus.Info);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
}
