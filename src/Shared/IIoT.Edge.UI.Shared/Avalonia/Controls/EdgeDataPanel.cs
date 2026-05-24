using System.Collections;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeDataPanel : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(Title));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(Content));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<EdgeDataPanel, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<IDataTemplate?> HeaderTemplateProperty =
        AvaloniaProperty.Register<EdgeDataPanel, IDataTemplate?>(nameof(HeaderTemplate));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<EdgeDataPanel, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<object?> EmptyTitleProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(EmptyTitle));

    public static readonly StyledProperty<object?> EmptyMessageProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(EmptyMessage));

    public static readonly StyledProperty<object?> ActionContentProperty =
        AvaloniaProperty.Register<EdgeDataPanel, object?>(nameof(ActionContent));

    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EdgeDataPanel, bool>(nameof(IsEmpty));

    static EdgeDataPanel()
    {
        IsEmptyProperty.Changed.AddClassHandler<EdgeDataPanel>((panel, _) => panel.UpdatePseudoClasses());
    }

    public EdgeDataPanel()
    {
        UpdatePseudoClasses();
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public IDataTemplate? HeaderTemplate
    {
        get => GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
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

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        if (IsEmpty)
        {
            PseudoClasses.Add(":empty");
            return;
        }

        PseudoClasses.Remove(":empty");
    }
}
