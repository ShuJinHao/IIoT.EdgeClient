using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// Shared parameter group surface. Business pages provide real data only.
/// </summary>
public class EdgeParameterPanel : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeParameterPanel, object?>(nameof(Title));

    public static readonly StyledProperty<object?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeParameterPanel, object?>(nameof(Subtitle));

    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<EdgeParameterPanel, object?>(nameof(Content));

    static EdgeParameterPanel()
    {
        SubtitleProperty.Changed.AddClassHandler<EdgeParameterPanel>((panel, _) => panel.UpdatePseudoClasses());
    }

    public EdgeParameterPanel()
    {
        UpdatePseudoClasses();
    }

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

    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        SetPseudoClass(":has-subtitle", HasVisibleContent(Subtitle));
        SetClass("has-subtitle", HasVisibleContent(Subtitle));
    }

    private static bool HasVisibleContent(object? content)
    {
        return content switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    private void SetPseudoClass(string name, bool enabled)
    {
        if (enabled)
        {
            PseudoClasses.Add(name);
        }
        else
        {
            PseudoClasses.Remove(name);
        }
    }

    private void SetClass(string name, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(name);
        }
        else
        {
            Classes.Remove(name);
        }
    }
}
