using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Metadata;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// Shared label/value row for editable parameter and diagnostic fields.
/// </summary>
public class EdgeFieldRow : TemplatedControl
{
    public static readonly StyledProperty<object?> LabelProperty =
        AvaloniaProperty.Register<EdgeFieldRow, object?>(nameof(Label));

    public static readonly StyledProperty<object?> DescriptionProperty =
        AvaloniaProperty.Register<EdgeFieldRow, object?>(nameof(Description));

    public static readonly StyledProperty<object?> ValueContentProperty =
        AvaloniaProperty.Register<EdgeFieldRow, object?>(nameof(ValueContent));

    static EdgeFieldRow()
    {
        DescriptionProperty.Changed.AddClassHandler<EdgeFieldRow>((row, _) => row.UpdatePseudoClasses());
    }

    public EdgeFieldRow()
    {
        UpdatePseudoClasses();
    }

    public object? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    [Content]
    public object? ValueContent
    {
        get => GetValue(ValueContentProperty);
        set => SetValue(ValueContentProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        SetPseudoClass(":has-description", HasVisibleContent(Description));
        SetClass("has-description", HasVisibleContent(Description));
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
