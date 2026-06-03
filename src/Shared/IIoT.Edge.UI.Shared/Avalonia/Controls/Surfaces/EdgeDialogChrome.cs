using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using System.Windows.Input;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeDialogChrome : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, object?>(nameof(Title));

    public static readonly StyledProperty<object?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, object?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> HeaderActionContentProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, object?>(nameof(HeaderActionContent));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, object?>(nameof(Content));

    public static readonly StyledProperty<object?> FooterContentProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, object?>(nameof(FooterContent));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<bool> CloseTopLevelOnCloseProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, bool>(nameof(CloseTopLevelOnClose), true);

    public static readonly StyledProperty<bool> MoveTopLevelOnHeaderDragProperty =
        AvaloniaProperty.Register<EdgeDialogChrome, bool>(nameof(MoveTopLevelOnHeaderDrag), true);

    private Control? header;
    private Button? closeButton;

    public event EventHandler? CloseRequested;

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

    public object? HeaderActionContent
    {
        get => GetValue(HeaderActionContentProperty);
        set => SetValue(HeaderActionContentProperty, value);
    }

    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public bool CloseTopLevelOnClose
    {
        get => GetValue(CloseTopLevelOnCloseProperty);
        set => SetValue(CloseTopLevelOnCloseProperty, value);
    }

    public bool MoveTopLevelOnHeaderDrag
    {
        get => GetValue(MoveTopLevelOnHeaderDragProperty);
        set => SetValue(MoveTopLevelOnHeaderDragProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (header is not null)
        {
            header.PointerPressed -= OnHeaderPointerPressed;
        }

        if (closeButton is not null)
        {
            closeButton.Click -= OnCloseButtonClick;
        }

        base.OnApplyTemplate(e);

        header = e.NameScope.Find<Control>("PART_Header");
        closeButton = e.NameScope.Find<Button>("PART_CloseButton");

        if (header is not null)
        {
            header.PointerPressed += OnHeaderPointerPressed;
        }

        if (closeButton is not null)
        {
            closeButton.Click += OnCloseButtonClick;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!MoveTopLevelOnHeaderDrag)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);

        var closeCommand = CloseCommand;
        if (closeCommand is not null && closeCommand.CanExecute(null))
        {
            closeCommand.Execute(null);
        }

        if (!CloseTopLevelOnClose)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.Close();
        }
    }
}
