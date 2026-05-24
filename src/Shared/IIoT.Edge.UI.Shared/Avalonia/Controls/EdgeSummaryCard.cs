using System.Collections;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 摘要卡片基座，用于少量键值信息，不承载完整明细列表。
/// </summary>
public class EdgeSummaryCard : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeSummaryCard, object?>(nameof(Title));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EdgeSummaryCard, IEnumerable?>(nameof(ItemsSource));

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
}
