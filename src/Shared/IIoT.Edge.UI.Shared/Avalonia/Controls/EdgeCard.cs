using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

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

    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<EdgeCard, object?>(nameof(Header));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<EdgeCard, object?>(nameof(Footer));

    public static readonly StyledProperty<EdgeVisualVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeCard, EdgeVisualVariant>(nameof(Variant), EdgeVisualVariant.Default);

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
    }

    public EdgeCard()
    {
        UpdateVariantClass();
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
}
