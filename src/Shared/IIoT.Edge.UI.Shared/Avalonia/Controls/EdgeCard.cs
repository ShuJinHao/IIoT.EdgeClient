using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeCardElevation
{
    Flat,
    Card,
    Float
}

public enum EdgeCardSurface
{
    Card,
    Wash,
    Raised,
    Accent,
    Transparent
}

public enum EdgeCardPaddingMode
{
    None,
    Compact,
    Normal,
    Spacious
}

/// <summary>
/// 统一卡片容器，承载页面中的面板、摘要块和轻量分区。
/// </summary>
public class EdgeCard : ContentControl
{
    private static readonly string[] VariantClasses =
    [
        "default",
        "emphasis",
        "warning",
        "danger",
        "success"
    ];

    private static readonly string[] ElevationClasses =
    [
        "elevation-flat",
        "elevation-card",
        "elevation-float"
    ];

    private static readonly string[] SurfaceClasses =
    [
        "surface-card",
        "surface-wash",
        "surface-raised",
        "surface-accent",
        "surface-transparent"
    ];

    private static readonly string[] PaddingModeClasses =
    [
        "padding-none",
        "padding-compact",
        "padding-normal",
        "padding-spacious"
    ];

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<EdgeCard, object?>(nameof(Header));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<EdgeCard, object?>(nameof(Footer));

    public static readonly StyledProperty<EdgeVisualVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeCard, EdgeVisualVariant>(nameof(Variant), EdgeVisualVariant.Default);

    public static readonly StyledProperty<EdgeCardElevation> ElevationProperty =
        AvaloniaProperty.Register<EdgeCard, EdgeCardElevation>(nameof(Elevation), EdgeCardElevation.Card);

    public static readonly StyledProperty<EdgeCardSurface> SurfaceProperty =
        AvaloniaProperty.Register<EdgeCard, EdgeCardSurface>(nameof(Surface), EdgeCardSurface.Card);

    public static readonly StyledProperty<EdgeCardPaddingMode> PaddingModeProperty =
        AvaloniaProperty.Register<EdgeCard, EdgeCardPaddingMode>(nameof(PaddingMode), EdgeCardPaddingMode.Normal);

    public static readonly StyledProperty<IBrush?> CardBackgroundProperty =
        AvaloniaProperty.Register<EdgeCard, IBrush?>(nameof(CardBackground));

    public static readonly StyledProperty<IBrush?> CardBorderBrushProperty =
        AvaloniaProperty.Register<EdgeCard, IBrush?>(nameof(CardBorderBrush));

    public static readonly StyledProperty<Thickness> CardBorderThicknessProperty =
        AvaloniaProperty.Register<EdgeCard, Thickness>(nameof(CardBorderThickness), new Thickness(1));

    public static readonly StyledProperty<CornerRadius> CardCornerRadiusProperty =
        AvaloniaProperty.Register<EdgeCard, CornerRadius>(nameof(CardCornerRadius), new CornerRadius(8));

    public static readonly StyledProperty<Thickness> CardPaddingProperty =
        AvaloniaProperty.Register<EdgeCard, Thickness>(nameof(CardPadding), new Thickness(16));

    public static readonly StyledProperty<BoxShadows> CardShadowProperty =
        AvaloniaProperty.Register<EdgeCard, BoxShadows>(nameof(CardShadow), default);

    static EdgeCard()
    {
        VariantProperty.Changed.AddClassHandler<EdgeCard>((control, _) => control.UpdateVariantClass());
        ElevationProperty.Changed.AddClassHandler<EdgeCard>((control, _) => control.UpdateElevationClass());
        SurfaceProperty.Changed.AddClassHandler<EdgeCard>((control, _) => control.UpdateSurfaceClass());
        PaddingModeProperty.Changed.AddClassHandler<EdgeCard>((control, _) => control.UpdatePaddingModeClass());
    }

    public EdgeCard()
    {
        UpdateVariantClass();
        UpdateElevationClass();
        UpdateSurfaceClass();
        UpdatePaddingModeClass();
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public EdgeVisualVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public EdgeCardElevation Elevation
    {
        get => GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    public EdgeCardSurface Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public EdgeCardPaddingMode PaddingMode
    {
        get => GetValue(PaddingModeProperty);
        set => SetValue(PaddingModeProperty, value);
    }

    public IBrush? CardBackground
    {
        get => GetValue(CardBackgroundProperty);
        set => SetValue(CardBackgroundProperty, value);
    }

    public IBrush? CardBorderBrush
    {
        get => GetValue(CardBorderBrushProperty);
        set => SetValue(CardBorderBrushProperty, value);
    }

    public Thickness CardBorderThickness
    {
        get => GetValue(CardBorderThicknessProperty);
        set => SetValue(CardBorderThicknessProperty, value);
    }

    public CornerRadius CardCornerRadius
    {
        get => GetValue(CardCornerRadiusProperty);
        set => SetValue(CardCornerRadiusProperty, value);
    }

    public Thickness CardPadding
    {
        get => GetValue(CardPaddingProperty);
        set => SetValue(CardPaddingProperty, value);
    }

    public BoxShadows CardShadow
    {
        get => GetValue(CardShadowProperty);
        set => SetValue(CardShadowProperty, value);
    }

    private void UpdateVariantClass()
    {
        foreach (var className in VariantClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Variant.ToString().ToLowerInvariant());
    }

    private void UpdateElevationClass()
    {
        foreach (var className in ElevationClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add($"elevation-{Elevation.ToString().ToLowerInvariant()}");
    }

    private void UpdateSurfaceClass()
    {
        foreach (var className in SurfaceClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add($"surface-{Surface.ToString().ToLowerInvariant()}");
    }

    private void UpdatePaddingModeClass()
    {
        foreach (var className in PaddingModeClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add($"padding-{PaddingMode.ToString().ToLowerInvariant()}");
    }
}
