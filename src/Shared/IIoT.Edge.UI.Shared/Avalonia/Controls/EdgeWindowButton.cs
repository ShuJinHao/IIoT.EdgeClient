using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeWindowButtonKind
{
    Normal,
    Close
}

public enum EdgeWindowButtonAction
{
    None,
    Minimize,
    MaximizeRestore,
    Close
}

public class EdgeWindowButton : Button
{
    private static readonly string[] KindClasses =
    [
        "window-normal",
        "window-close"
    ];

    public static readonly StyledProperty<EdgeWindowButtonKind> KindProperty =
        AvaloniaProperty.Register<EdgeWindowButton, EdgeWindowButtonKind>(nameof(Kind), EdgeWindowButtonKind.Normal);

    public static readonly StyledProperty<EdgeWindowButtonAction> ActionProperty =
        AvaloniaProperty.Register<EdgeWindowButton, EdgeWindowButtonAction>(nameof(Action), EdgeWindowButtonAction.None);

    static EdgeWindowButton()
    {
        KindProperty.Changed.AddClassHandler<EdgeWindowButton>((control, _) => control.UpdateKindClass());
        ActionProperty.Changed.AddClassHandler<EdgeWindowButton>((control, _) => control.UpdateAction());
    }

    public EdgeWindowButton()
    {
        UpdateKindClass();
        UpdateAction();
    }

    public EdgeWindowButtonKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public EdgeWindowButtonAction Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    private void UpdateAction()
    {
        Content = Action switch
        {
            EdgeWindowButtonAction.Minimize => "─",
            EdgeWindowButtonAction.MaximizeRestore => "☐",
            EdgeWindowButtonAction.Close => "✕",
            _ => Content
        };

        UpdateKindClass();
    }

    private void UpdateKindClass()
    {
        foreach (var className in KindClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Kind == EdgeWindowButtonKind.Close || Action == EdgeWindowButtonAction.Close
            ? "window-close"
            : "window-normal");
    }
}
