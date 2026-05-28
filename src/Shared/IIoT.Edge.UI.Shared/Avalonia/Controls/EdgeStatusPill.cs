using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 轻量状态胶囊，用于运行态、告警级别和列表状态。
/// </summary>
public class EdgeStatusPill : TemplatedControl
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
        AvaloniaProperty.Register<EdgeStatusPill, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Default);

    public static readonly StyledProperty<object?> TextProperty =
        AvaloniaProperty.Register<EdgeStatusPill, object?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<EdgeStatusPill, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> DotFillProperty =
        AvaloniaProperty.Register<EdgeStatusPill, IBrush?>(nameof(DotFill));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<EdgeStatusPill, IBrush?>(nameof(TextBrush));

    static EdgeStatusPill()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusPill>((control, _) => control.UpdateStatusClass());
    }

    public EdgeStatusPill()
    {
        UpdateStatusClass();
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public object? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? DotFill
    {
        get => GetValue(DotFillProperty);
        set => SetValue(DotFillProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
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
