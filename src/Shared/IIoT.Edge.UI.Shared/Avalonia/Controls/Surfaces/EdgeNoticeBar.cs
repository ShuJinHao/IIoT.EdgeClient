using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeNoticeBar : TemplatedControl
{
    private static readonly string[] StatusClasses =
    [
        "default",
        "running",
        "idle",
        "stopped",
        "offline",
        "info",
        "cache",
        "warning",
        "error"
    ];

    public static readonly StyledProperty<EdgeVisualStatus> StatusProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Info);

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(Icon));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(Content));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeNoticeBar, object?>(nameof(ActionContent));

    static EdgeNoticeBar()
    {
        StatusProperty.Changed.AddClassHandler<EdgeNoticeBar>((bar, _) => bar.UpdateStatusClass());
    }

    public EdgeNoticeBar()
    {
        UpdateStatusClass();
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
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

    private void UpdateStatusClass()
    {
        foreach (var className in StatusClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Status.ToString().ToLowerInvariant());
    }
}
