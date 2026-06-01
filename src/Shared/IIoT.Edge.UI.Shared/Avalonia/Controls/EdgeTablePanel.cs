using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeTablePanelSurface
{
    Card,
    Transparent
}

public enum EdgeTablePanelDensity
{
    Compact,
    Normal,
    Diagnostic
}

public class EdgeTablePanel : TemplatedControl
{
    public static readonly StyledProperty<EdgeTablePanelSurface> SurfaceProperty =
        AvaloniaProperty.Register<EdgeTablePanel, EdgeTablePanelSurface>(
            nameof(Surface),
            EdgeTablePanelSurface.Card);

    public static readonly StyledProperty<EdgeTablePanelDensity> DensityProperty =
        AvaloniaProperty.Register<EdgeTablePanel, EdgeTablePanelDensity>(
            nameof(Density),
            EdgeTablePanelDensity.Normal);

    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(Title));

    public static readonly StyledProperty<object?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> StatusContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(StatusContent));

    public static readonly StyledProperty<object?> FilterContentProperty =
        AvaloniaProperty.Register<EdgeTablePanel, object?>(nameof(FilterContent));

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
        DensityProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdateDensityClasses());
        SubtitleProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        StatusContentProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        FilterContentProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        ActionContentProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        ShowContentWhenEmptyProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        IsEmptyProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
        HasErrorProperty.Changed.AddClassHandler<EdgeTablePanel>((panel, _) => panel.UpdatePseudoClasses());
    }

    public EdgeTablePanel()
    {
        UpdateSurfaceClasses();
        UpdateDensityClasses();
        UpdatePseudoClasses();
    }

    public EdgeTablePanelSurface Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public EdgeTablePanelDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
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
        var hasFilter = HasVisibleContent(FilterContent);
        var hasActions = HasVisibleContent(ActionContent);
        var hasStatus = HasVisibleContent(StatusContent);

        SetPseudoClass(":empty", IsEmpty && !ShowContentWhenEmpty);
        SetPseudoClass(":error", HasError);
        SetPseudoClass(":has-subtitle", HasVisibleContent(Subtitle));
        SetPseudoClass(":has-status", hasStatus);
        SetPseudoClass(":has-toolbar", hasFilter || hasActions);
        SetPseudoClass(":has-filter", hasFilter);
        SetPseudoClass(":has-actions", hasActions);
        SetPseudoClass(":actions-only", hasActions && !hasFilter && !hasStatus);
        SetClass("has-subtitle", HasVisibleContent(Subtitle));
        SetClass("has-status", hasStatus);
        SetClass("has-toolbar", hasFilter || hasActions);
        SetClass("has-filter", hasFilter);
        SetClass("has-actions", hasActions);
        SetClass("actions-only", hasActions && !hasFilter && !hasStatus);
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

    private void UpdateDensityClasses()
    {
        SetClass("density-compact", Density == EdgeTablePanelDensity.Compact);
        SetClass("density-normal", Density == EdgeTablePanelDensity.Normal);
        SetClass("density-diagnostic", Density == EdgeTablePanelDensity.Diagnostic);
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
