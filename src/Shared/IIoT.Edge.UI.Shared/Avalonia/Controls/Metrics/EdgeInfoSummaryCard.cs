using System.Collections;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 页面级信息摘要卡，统一标题、键值摘要和提示消息，避免业务页手写卡片内脏。
/// </summary>
public class EdgeInfoSummaryCard : EdgeStatusControlBase
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, object?>(nameof(Title));

    public static readonly StyledProperty<object?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, object?>(nameof(Subtitle));

    public static readonly StyledProperty<IEnumerable?> SummaryItemsProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, IEnumerable?>(nameof(SummaryItems));

    public static readonly StyledProperty<double> SummaryItemMinWidthProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, double>(nameof(SummaryItemMinWidth), 0d);

    public static readonly StyledProperty<object?> NoticeMessageProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, object?>(nameof(NoticeMessage));

    public static readonly StyledProperty<EdgeVisualStatus> NoticeStatusProperty =
        AvaloniaProperty.Register<EdgeInfoSummaryCard, EdgeVisualStatus>(nameof(NoticeStatus), EdgeVisualStatus.Info);

    public static readonly DirectProperty<EdgeInfoSummaryCard, bool> HasNoticeProperty =
        AvaloniaProperty.RegisterDirect<EdgeInfoSummaryCard, bool>(nameof(HasNotice), card => card.HasNotice);

    private bool _hasNotice;

    static EdgeInfoSummaryCard()
    {
        NoticeMessageProperty.Changed.AddClassHandler<EdgeInfoSummaryCard>((control, _) => control.UpdateNoticeState());
    }

    public EdgeInfoSummaryCard()
    {
        UpdateNoticeState();
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

    public IEnumerable? SummaryItems
    {
        get => GetValue(SummaryItemsProperty);
        set => SetValue(SummaryItemsProperty, value);
    }

    public double SummaryItemMinWidth
    {
        get => GetValue(SummaryItemMinWidthProperty);
        set => SetValue(SummaryItemMinWidthProperty, value);
    }

    public object? NoticeMessage
    {
        get => GetValue(NoticeMessageProperty);
        set => SetValue(NoticeMessageProperty, value);
    }

    public EdgeVisualStatus NoticeStatus
    {
        get => GetValue(NoticeStatusProperty);
        set => SetValue(NoticeStatusProperty, value);
    }

    public bool HasNotice
    {
        get => _hasNotice;
        private set => SetAndRaise(HasNoticeProperty, ref _hasNotice, value);
    }

    private void UpdateNoticeState()
        => HasNotice = NoticeMessage switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
}
