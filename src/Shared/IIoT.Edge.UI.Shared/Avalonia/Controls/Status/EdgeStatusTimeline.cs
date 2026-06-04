using System.Collections;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 标准状态时间线，统一分组标题、连接线、状态点、状态标签和空态。
/// </summary>
public class EdgeStatusTimeline : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeStatusTimeline, object?>(nameof(Title));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EdgeStatusTimeline, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EdgeStatusTimeline, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<object?> EmptyTitleProperty =
        AvaloniaProperty.Register<EdgeStatusTimeline, object?>(nameof(EmptyTitle));

    public static readonly StyledProperty<object?> EmptyMessageProperty =
        AvaloniaProperty.Register<EdgeStatusTimeline, object?>(nameof(EmptyMessage));

    public static readonly DirectProperty<EdgeStatusTimeline, bool> HasTitleProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusTimeline, bool>(nameof(HasTitle), timeline => timeline.HasTitle);

    public static readonly DirectProperty<EdgeStatusTimeline, bool> HasItemsProperty =
        AvaloniaProperty.RegisterDirect<EdgeStatusTimeline, bool>(nameof(HasItems), timeline => timeline.HasItems);

    private bool _hasTitle;
    private bool _hasItems = true;

    static EdgeStatusTimeline()
    {
        TitleProperty.Changed.AddClassHandler<EdgeStatusTimeline>((control, _) => control.UpdateTitleState());
        IsEmptyProperty.Changed.AddClassHandler<EdgeStatusTimeline>((control, _) => control.UpdateItemsState());
    }

    public EdgeStatusTimeline()
    {
        UpdateTitleState();
        UpdateItemsState();
    }

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

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
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

    public bool HasTitle
    {
        get => _hasTitle;
        private set => SetAndRaise(HasTitleProperty, ref _hasTitle, value);
    }

    public bool HasItems
    {
        get => _hasItems;
        private set => SetAndRaise(HasItemsProperty, ref _hasItems, value);
    }

    private void UpdateTitleState()
        => HasTitle = Title switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };

    private void UpdateItemsState()
        => HasItems = !IsEmpty;
}
