using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeLoginInput : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EdgeLoginInput, string?>(nameof(Text));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<EdgeLoginInput, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<char> PasswordCharProperty =
        AvaloniaProperty.Register<EdgeLoginInput, char>(nameof(PasswordChar));

    private TextBox? _input;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public char PasswordChar
    {
        get => GetValue(PasswordCharProperty);
        set => SetValue(PasswordCharProperty, value);
    }

    public bool FocusInput()
        => _input?.Focus() ?? Focus();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
    }
}
