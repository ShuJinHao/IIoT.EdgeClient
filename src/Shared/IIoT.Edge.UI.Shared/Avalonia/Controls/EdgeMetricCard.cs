using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 指标卡片基座，用于产量、良率、NG、连接数等摘要数据。
/// </summary>
public class EdgeMetricCard : TemplatedControl
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

    private static readonly string[] IconClasses =
    [
        "has-icon",
        "no-icon"
    ];

    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeMetricCard, object?>(nameof(Title));

    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<EdgeMetricCard, object?>(nameof(Value));

    public static readonly StyledProperty<object?> UnitProperty =
        AvaloniaProperty.Register<EdgeMetricCard, object?>(nameof(Unit));

    public static readonly StyledProperty<object?> DescriptionProperty =
        AvaloniaProperty.Register<EdgeMetricCard, object?>(nameof(Description));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<EdgeMetricCard, object?>(nameof(Icon));

    public static readonly StyledProperty<EdgeVisualStatus> StatusProperty =
        AvaloniaProperty.Register<EdgeMetricCard, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Default);

    static EdgeMetricCard()
    {
        StatusProperty.Changed.AddClassHandler<EdgeMetricCard>((control, _) => control.UpdateStatusClass());
        IconProperty.Changed.AddClassHandler<EdgeMetricCard>((control, _) => control.UpdateIconClass());
    }

    public EdgeMetricCard()
    {
        UpdateStatusClass();
        UpdateIconClass();
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public object? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private void UpdateStatusClass()
    {
        foreach (var className in StatusClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Status.ToString().ToLowerInvariant());
    }

    private void UpdateIconClass()
    {
        foreach (var className in IconClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Icon is null ? "no-icon" : "has-icon");
    }
}
