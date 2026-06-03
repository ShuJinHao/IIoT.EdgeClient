using Avalonia;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一状态标签，用于在线、运行、失败、缓存等短状态文本。
/// </summary>
public class EdgeStatusChip : EdgeStatusControlBase
{
    public static readonly StyledProperty<object?> TextProperty =
        AvaloniaProperty.Register<EdgeStatusChip, object?>(nameof(Text));

    public static readonly StyledProperty<EdgeVisualVariant> VariantProperty =
        AvaloniaProperty.Register<EdgeStatusChip, EdgeVisualVariant>(nameof(Variant), EdgeVisualVariant.Default);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<EdgeStatusChip, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<EdgeStatusChip, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<IBrush?> DotFillProperty =
        AvaloniaProperty.Register<EdgeStatusChip, IBrush?>(nameof(DotFill));

    public static readonly StyledProperty<Thickness> ChipPaddingProperty =
        AvaloniaProperty.Register<EdgeStatusChip, Thickness>(nameof(ChipPadding), new Thickness(8, 2));

    public static readonly StyledProperty<bool> ShowDotProperty =
        AvaloniaProperty.Register<EdgeStatusChip, bool>(nameof(ShowDot));

    static EdgeStatusChip()
    {
        StatusProperty.OverrideDefaultValue<EdgeStatusChip>(EdgeVisualStatus.Info);
    }

    public object? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EdgeVisualVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? DotFill
    {
        get => GetValue(DotFillProperty);
        set => SetValue(DotFillProperty, value);
    }

    public Thickness ChipPadding
    {
        get => GetValue(ChipPaddingProperty);
        set => SetValue(ChipPaddingProperty, value);
    }

    public bool ShowDot
    {
        get => GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }
}
