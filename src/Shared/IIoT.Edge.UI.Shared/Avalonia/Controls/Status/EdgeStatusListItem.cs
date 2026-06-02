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

    public static readonly DirectProperty<EdgeStatusListItem, bool> HasDescriptionProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusListItem, bool>(nameof(HasDescription), item => item.HasDescription);

    public static readonly DirectProperty<EdgeStatusListItem, bool> HasDetailProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusListItem, bool>(nameof(HasDetail), item => item.HasDetail);

    public static readonly DirectProperty<EdgeStatusListItem, bool> HasStatusTextProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusListItem, bool>(nameof(HasStatusText), item => item.HasStatusText);

    public static readonly DirectProperty<EdgeStatusListItem, bool> HasActionContentProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusListItem, bool>(nameof(HasActionContent), item => item.HasActionContent);

    private bool _hasDescription;
    private bool _hasDetail;
    private bool _hasStatusText;
    private bool _hasActionContent;

    static EdgeStatusListItem()
    {
        StatusProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateStatusClass());
        DescriptionProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateContentState());
        DetailProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateContentState());
        StatusTextProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateContentState());
        ActionContentProperty.Changed.AddClassHandler<EdgeStatusListItem>((control, _) => control.UpdateContentState());
    }

    public EdgeStatusListItem()
    {
        UpdateStatusClass();
        UpdateContentState();
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

    public bool HasDescription
    {
        get => _hasDescription;
        private set => SetAndRaise(HasDescriptionProperty, ref _hasDescription, value);
    }

    public bool HasDetail
    {
        get => _hasDetail;
        private set => SetAndRaise(HasDetailProperty, ref _hasDetail, value);
    }

    public bool HasStatusText
    {
        get => _hasStatusText;
        private set => SetAndRaise(HasStatusTextProperty, ref _hasStatusText, value);
    }

    public bool HasActionContent
    {
        get => _hasActionContent;
        private set => SetAndRaise(HasActionContentProperty, ref _hasActionContent, value);
    }

    private void UpdateStatusClass()
    {
        foreach (var className in StatusClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Status.ToString().ToLowerInvariant());
    }

    private void UpdateContentState()
    {
        HasDescription = HasVisibleContent(Description);
        HasDetail = HasVisibleContent(Detail);
        HasStatusText = HasVisibleContent(StatusText);
        HasActionContent = ActionContent is not null;

        SetClass("has-description", HasDescription);
        SetClass("has-detail", HasDetail);
        SetClass("has-status-text", HasStatusText);
        SetClass("has-action", HasActionContent);
    }

    private static bool HasVisibleContent(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private void SetClass(string className, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(className);
        }
        else
        {
            Classes.Remove(className);
        }
    }
}
