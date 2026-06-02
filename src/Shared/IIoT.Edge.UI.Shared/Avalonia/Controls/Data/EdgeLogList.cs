using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 右侧信息栏使用的日志列表外壳，只承载视觉结构，不处理日志业务来源。
/// </summary>
public class EdgeLogList : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(Title));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EdgeLogList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<EdgeLogList, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EdgeLogList, bool>(nameof(IsEmpty), true);

    public static readonly StyledProperty<string?> EmptyTitleProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(EmptyTitle));

    public static readonly StyledProperty<string?> EmptyMessageProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(EmptyMessage));

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<EdgeLogList, ICommand?>(nameof(ClearCommand));

    public static readonly StyledProperty<string?> ClearTextProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(ClearText));

    public static readonly StyledProperty<string?> TimeHeaderProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(TimeHeader));

    public static readonly StyledProperty<string?> LevelHeaderProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(LevelHeader));

    public static readonly StyledProperty<string?> MessageHeaderProperty =
        AvaloniaProperty.Register<EdgeLogList, string?>(nameof(MessageHeader));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public string? EmptyTitle
    {
        get => GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string? EmptyMessage
    {
        get => GetValue(EmptyMessageProperty);
        set => SetValue(EmptyMessageProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public string? ClearText
    {
        get => GetValue(ClearTextProperty);
        set => SetValue(ClearTextProperty, value);
    }

    public string? TimeHeader
    {
        get => GetValue(TimeHeaderProperty);
        set => SetValue(TimeHeaderProperty, value);
    }

    public string? LevelHeader
    {
        get => GetValue(LevelHeaderProperty);
        set => SetValue(LevelHeaderProperty, value);
    }

    public string? MessageHeader
    {
        get => GetValue(MessageHeaderProperty);
        set => SetValue(MessageHeaderProperty, value);
    }
}
