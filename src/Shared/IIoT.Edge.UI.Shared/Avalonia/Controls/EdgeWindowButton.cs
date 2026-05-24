using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeWindowButtonKind
{
    Normal,
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

    static EdgeWindowButton()
    {
        KindProperty.Changed.AddClassHandler<EdgeWindowButton>((control, _) => control.UpdateKindClass());
    }

    public EdgeWindowButton()
    {
        UpdateKindClass();
    }

    public EdgeWindowButtonKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void UpdateKindClass()
    {
        foreach (var className in KindClasses)
        {
            Classes.Remove(className);
        }

        Classes.Add(Kind == EdgeWindowButtonKind.Close ? "window-close" : "window-normal");
    }
}
