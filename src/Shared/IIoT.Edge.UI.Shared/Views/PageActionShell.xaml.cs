using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace IIoT.Edge.UI.Shared.Views;

/// <summary>
/// 页面操作壳控件，统一承载页面头部、操作区和主体内容区。
/// </summary>
[ContentProperty(nameof(PageContent))]
public class PageActionShell : UserControl
{
    public static readonly DependencyProperty HeaderContentProperty = DependencyProperty.Register(
        nameof(HeaderContent),
        typeof(object),
        typeof(PageActionShell),
        new PropertyMetadata(null));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent),
        typeof(object),
        typeof(PageActionShell),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PageContentProperty = DependencyProperty.Register(
        nameof(PageContent),
        typeof(object),
        typeof(PageActionShell),
        new PropertyMetadata(null));

    public PageActionShell()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var border = new Border { Padding = new Thickness(16, 12, 16, 12) };
        var headerGrid = new Grid();

        var headerControl = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerControl.SetBinding(ContentControl.ContentProperty,
            new Binding(nameof(HeaderContent)) { Source = this });

        var actionControl = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        actionControl.SetBinding(ContentControl.ContentProperty,
            new Binding(nameof(ActionContent)) { Source = this });

        headerGrid.Children.Add(headerControl);
        headerGrid.Children.Add(actionControl);
        border.Child = headerGrid;
        Grid.SetRow(border, 0);
        grid.Children.Add(border);

        var banner = new FeedbackBanner { Margin = new Thickness(16, 0, 16, 12) };
        Grid.SetRow(banner, 1);
        grid.Children.Add(banner);

        var contentPresenter = new ContentPresenter();
        contentPresenter.SetBinding(ContentPresenter.ContentProperty,
            new Binding(nameof(PageContent)) { Source = this });
        Grid.SetRow(contentPresenter, 2);
        grid.Children.Add(contentPresenter);

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
