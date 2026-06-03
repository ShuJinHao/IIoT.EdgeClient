using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 右侧日志列表的单行视觉控件，只承载时间、等级、消息展示。
/// </summary>
public class EdgeLogListItem : TemplatedControl
{
    public static readonly StyledProperty<string?> TimeTextProperty =
        AvaloniaProperty.Register<EdgeLogListItem, string?>(nameof(TimeText));

    public static readonly StyledProperty<string?> LevelTextProperty =
        AvaloniaProperty.Register<EdgeLogListItem, string?>(nameof(LevelText));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EdgeLogListItem, string?>(nameof(Message));

    public static readonly StyledProperty<int> MaxMessageLinesProperty =
        AvaloniaProperty.Register<EdgeLogListItem, int>(nameof(MaxMessageLines), 2);

    public static readonly StyledProperty<EdgeVisualStatus> StatusProperty =
        AvaloniaProperty.Register<EdgeLogListItem, EdgeVisualStatus>(nameof(Status), EdgeVisualStatus.Default);

    public static readonly StyledProperty<bool> ShowTimeColumnProperty =
        AvaloniaProperty.Register<EdgeLogListItem, bool>(nameof(ShowTimeColumn), true);

    public string? TimeText
    {
        get => GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    public string? LevelText
    {
        get => GetValue(LevelTextProperty);
        set => SetValue(LevelTextProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public int MaxMessageLines
    {
        get => GetValue(MaxMessageLinesProperty);
        set => SetValue(MaxMessageLinesProperty, value);
    }

    public EdgeVisualStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool ShowTimeColumn
    {
        get => GetValue(ShowTimeColumnProperty);
        set => SetValue(ShowTimeColumnProperty, value);
    }
}
