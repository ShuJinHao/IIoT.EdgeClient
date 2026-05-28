using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeTablePanelSurface
{
    Card,
    Transparent
}

public class EdgeTablePanel : TemplatedControl
{
    public static readonly StyledProperty<EdgeTablePanelSurface> SurfaceProperty =
        AvaloniaProperty.Register<EdgeTablePanel, EdgeTablePanelSurface>(
            nameof(Surface),
            EdgeTablePanelSurface.Card);

    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(Title));

    public static readonly StyledProperty<object?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> StatusContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(StatusContent));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(ActionContent));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(Content));

    // 默认 0：表格高度自适应内容行数，与 EdgeDataPanel 保持一致
    public static readonly StyledProperty<double> ContentMinHeightProperty =
        AvaloniaProperty.Register<EdgeTablePanel, double>(nameof(ContentMinHeight), 0d);

    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(FooterContent));

    public static readonly StyledProperty<bool> ShowContentWhenEmptyProperty =
        AvaloniaProperty.Register<EdgeTablePanel, bool>(nameof(ShowContentWhenEmpty));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EdgeTablePanel, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<EdgeTablePanel, bool>(nameof(HasError));

    public static readonly StyledProperty<object?> ErrorMessageProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(ErrorMessage));

    public static readonly StyledProperty<object?> EmptyTitleProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(EmptyTitle));

    public static readonly StyledProperty<object?> EmptyMessageProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(EmptyMessage));

    static EdgeTablePanel()
    {
        SurfaceProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdateSurfaceClasses());
        SubtitleProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        StatusContentProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        ShowContentWhenEmptyProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        IsEmptyProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        HasErrorProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
    }

    public EdgeTablePanel()
    {
        UpdateSurfaceClasses();
        UpdatePseudoClasses();
    }

    public EdgeTablePanelSurface Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public object? StatusContent
    {
        get => GetValue(StatusContentProperty);
        set => SetValue(StatusContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public double ContentMinHeight
    {
        get => GetValue(ContentMinHeightProperty);
        set => SetValue(ContentMinHeightProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public bool ShowContentWhenEmpty
    {
        get => GetValue(ShowContentWhenEmptyProperty);
        set => SetValue(ShowContentWhenEmptyProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public bool HasError
    {
        get => GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    public object? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public object? EmptyTitle
    {
        get => GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public object? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        SetPseudoClass(":empty", IsEmpty && !ShowContentWhenEmpty);
        SetPseudoClass(":error", HasError);
        SetPseudoClass(":has-subtitle", HasVisibleContent(Subtitle));
        SetPseudoClass(":has-status", HasVisibleContent(StatusContent));
        SetClass("has-subtitle", HasVisibleContent(Subtitle));
        SetClass("has-status", HasVisibleContent(StatusContent));
        SetClass("empty-content-visible", ShowContentWhenEmpty && IsEmpty && !HasError);
    }

    private static bool HasVisibleContent(object? content)
    {
        return content switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private void UpdateSurfaceClasses()
    {
        SetClass("surface-card", Surface == EdgeTablePanelSurface.Card);
        SetClass("surface-transparent", Surface == EdgeTablePanelSurface.Transparent);
    }

    private void SetPseudoClass(string name, bool enabled)
    {
        if (enabled)
        {
            PseudoClasses.Add(name);
        }
        else
        {
            PseudoClasses.Remove(name);
        }
    }

    private void SetClass(string name, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(name);
        }
        else
        {
            Classes.Remove(name);
        }
    }
}
