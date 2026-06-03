using System.Collections;
using Avalonia;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 指标卡片基座，用于产量、良率、NG、连接数等摘要数据。
/// </summary>
public class EdgeMetricCard : EdgeStatusControlBase
{
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

    public static readonly StyledProperty<IEnumerable?> SummaryItemsProperty =
        AvaloniaProperty.Register<EdgeMetricCard, IEnumerable?>(nameof(SummaryItems));

    public static readonly DirectProperty<EdgeMetricCard, bool> HasSummaryItemsProperty =
        AvaloniaProperty.RegisterDirect<EdgeMetricCard, bool>(nameof(HasSummaryItems), card => card.HasSummaryItems);

    private bool _hasSummaryItems;

    static EdgeMetricCard()
    {
        IconProperty.Changed.AddClassHandler<EdgeMetricCard>((control, _) => control.UpdateIconClass());
        SummaryItemsProperty.Changed.AddClassHandler<EdgeMetricCard>((control, _) => control.UpdateSummaryItemsState());
    }

    public EdgeMetricCard()
    {
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

    public IEnumerable? SummaryItems
    {
        get => GetValue(SummaryItemsProperty);
        set => SetValue(SummaryItemsProperty, value);
    }

    public bool HasSummaryItems
    {
        get => _hasSummaryItems;
        private set => SetAndRaise(HasSummaryItemsProperty, ref _hasSummaryItems, value);
    }

    private void UpdateIconClass()
    {
        foreach (var className in IconClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Icon is null ? "no-icon" : "has-icon");
    }

    private void UpdateSummaryItemsState()
    {
        HasSummaryItems = HasAnyItem(SummaryItems);
    }

    private static bool HasAnyItem(IEnumerable? items)
    {
        if (items is null)
        {
            return false;
        }

        var enumerator = items.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
