using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一状态圆点，用语义状态 token 表达在线、离线、告警等状态。
/// </summary>
public class EdgeStatusDot : TemplatedControl
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
        AvaloniaProperty.Register<EdgeStatusDot, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Info);

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<EdgeStatusDot, double>(nameof(Size), 8);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<EdgeStatusDot, IBrush?>(nameof(Fill));

    static EdgeStatusDot()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusDot>((control, _) => control.UpdateStatusClass());
    }

    public EdgeStatusDot()
    {
        UpdateStatusClass();
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
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

    private void UpdateStatusClass()
    {
        foreach (var className in StatusClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Status.ToString().ToLowerInvariant());
    }
}
