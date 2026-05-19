using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Views;

/// <summary>
/// Avalonia 空态视图，供迁移后的宿主和页面复用。
/// </summary>
public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(
            nameof(Title),
            "暂无数据");

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<EmptyStateView, string>(
            nameof(Message),
            "当前没有可展示的真实数据。");

    public EmptyStateView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
