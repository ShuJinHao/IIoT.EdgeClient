using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Layout;

namespace IIoT.Edge.UI.Shared.Views;

/// <summary>
/// 页面操作壳控件。保留旧插件二进制引用的类型入口，内部使用 Avalonia 控件实现。
/// </summary>
public class PageActionShell : UserControl
{
    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<PageActionShell, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<PageActionShell, object?>(nameof(ActionContent));

    public static readonly StyledProperty<object?> PageContentProperty =
        AvaloniaProperty.Register<PageActionShell, object?>(nameof(PageContent));

    public PageActionShell()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };

        var headerGrid = new Grid
        {
            Margin = new Thickness(16, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var header = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Bind(ContentControl.ContentProperty, new Binding(nameof(HeaderContent)) { Source = this });

        var actions = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Bind(ContentControl.ContentProperty, new Binding(nameof(ActionContent)) { Source = this });
        Grid.SetColumn(actions, 1);

        headerGrid.Children.Add(header);
        headerGrid.Children.Add(actions);

        var body = new ContentPresenter();
        body.Bind(ContentPresenter.ContentProperty, new Binding(nameof(PageContent)) { Source = this });
        Grid.SetRow(body, 1);

        grid.Children.Add(headerGrid);
        grid.Children.Add(body);
        Content = grid;
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public object? PageContent
    {
        get => GetValue(PageContentProperty);
        set => SetValue(PageContentProperty, value);
    }
}
