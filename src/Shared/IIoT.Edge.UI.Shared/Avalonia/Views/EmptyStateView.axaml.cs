using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Views;

public enum EmptyStateKind
{
    Empty,
    Loading,
    Error
}

/// <summary>
/// Avalonia 空态视图，供迁移后的宿主和页面复用。
/// </summary>
public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<EmptyStateKind> StateProperty =
        AvaloniaProperty.Register<EmptyStateView, EmptyStateKind>(
            nameof(State),
            EmptyStateKind.Empty);

    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, object?>(
            nameof(Title),
            "暂无数据");

    public static readonly StyledProperty<object?> MessageProperty =
        AvaloniaProperty.Register<EmptyStateView, object?>(
            nameof(Message),
            "当前没有可展示的真实数据。");

    static EmptyStateView()
    {
        StateProperty.Changed.AddClassHandler<EmptyStateView>((view, _) => view.UpdateStateClasses());
    }

    public EmptyStateView()
    {
        InitializeComponent();
        UpdateStateClasses();
    }

    public EmptyStateKind State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    private void UpdateStateClasses()
    {
        SetClass("empty", State == EmptyStateKind.Empty);
        SetClass("loading", State == EmptyStateKind.Loading);
        SetClass("error", State == EmptyStateKind.Error);
    }

    private void SetClass(string className, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(className);
        }
        else
        {
            Classes.Remove(className);
        }
    }
}
