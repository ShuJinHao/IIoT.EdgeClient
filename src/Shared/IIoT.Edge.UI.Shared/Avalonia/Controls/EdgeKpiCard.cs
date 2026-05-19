using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 统一 KPI 卡片，后续用于 Dashboard 和产能摘要。
/// </summary>
public class EdgeKpiCard : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EdgeKpiCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<EdgeKpiCard, string?>(nameof(Value));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<EdgeKpiCard, string?>(nameof(Unit));

    public static readonly StyledProperty<string?> TrendTextProperty =
        AvaloniaProperty.Register<EdgeKpiCard, string?>(nameof(TrendText));

    public static readonly StyledProperty<EdgeVisualStatus> TrendStatusProperty =
        AvaloniaProperty.Register<EdgeKpiCard, EdgeVisualStatus>(nameof(TrendStatus), EdgeVisualStatus.Info);

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string? TrendText
    {
        get => GetValue(TrendTextProperty);
        set => SetValue(TrendTextProperty, value);
    }

    public EdgeVisualStatus TrendStatus
    {
        get => GetValue(TrendStatusProperty);
        set => SetValue(TrendStatusProperty, value);
    }
}
