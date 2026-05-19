using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 右侧栏和状态列表的统一展示行，只负责视觉表达，不承载点击或业务动作。
/// </summary>
public class EdgeStatusListItem : TemplatedControl
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

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, string?>(nameof(Description));

    public static readonly StyledProperty<string?> DetailProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, string?>(nameof(Detail));

    public static readonly StyledProperty<EdgeVisualStatus> StatusProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Default);

    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, string?>(nameof(StatusText));

    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, Geometry?>(nameof(IconData));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, object?>(nameof(ActionContent));

    public static readonly StyledProperty<IBrush?> ItemBackgroundProperty =
        AvaloniaProperty.Register<EdgeStatusListItem, IBrush?>(nameof(ItemBackground));

    static EdgeStatusListItem()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateStatusClass());
    }

    public EdgeStatusListItem()
    {
        UpdateStatusClass();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string? StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public IBrush? ItemBackground
    {
        get => GetValue(ItemBackgroundProperty);
        set => SetValue(ItemBackgroundProperty, value);
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
