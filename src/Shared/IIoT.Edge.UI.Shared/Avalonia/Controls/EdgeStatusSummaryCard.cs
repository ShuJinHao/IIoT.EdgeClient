using Avalonia;
using Avalonia.Controls.Primitives;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// Shared diagnostic/status summary card with a stable compact text stack.
/// </summary>
public class EdgeStatusSummaryCard : TemplatedControl
{
    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<EdgeStatusSummaryCard, object?>(nameof(Title));

    public static readonly StyledProperty<object?> PrimaryTextProperty =
        AvaloniaProperty.Register<EdgeStatusSummaryCard, object?>(nameof(PrimaryText));

    public static readonly StyledProperty<object?> SecondaryTextProperty =
        AvaloniaProperty.Register<EdgeStatusSummaryCard, object?>(nameof(SecondaryText));

    public static readonly StyledProperty<object?> TertiaryTextProperty =
        AvaloniaProperty.Register<EdgeStatusSummaryCard, object?>(nameof(TertiaryText));

    public static readonly StyledProperty<object?> FooterTextProperty =
        AvaloniaProperty.Register<EdgeStatusSummaryCard, object?>(nameof(FooterText));

    static EdgeStatusSummaryCard()
    {
        PrimaryTextProperty.Changed.AddClassHandler<EdgeStatusSummaryCard>((card, _) => card.UpdatePseudoClasses());
        SecondaryTextProperty.Changed.AddClassHandler<EdgeStatusSummaryCard>((card, _) => card.UpdatePseudoClasses());
        TertiaryTextProperty.Changed.AddClassHandler<EdgeStatusSummaryCard>((card, _) => card.UpdatePseudoClasses());
        FooterTextProperty.Changed.AddClassHandler<EdgeStatusSummaryCard>((card, _) => card.UpdatePseudoClasses());
    }

    public EdgeStatusSummaryCard()
    {
        UpdatePseudoClasses();
    }

    public object? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? PrimaryText
    {
        get => GetValue(PrimaryTextProperty);
        set => SetValue(PrimaryTextProperty, value);
    }

    public object? SecondaryText
    {
        get => GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public object? TertiaryText
    {
        get => GetValue(TertiaryTextProperty);
        set => SetValue(TertiaryTextProperty, value);
    }

    public object? FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    private void UpdatePseudoClasses()
    {
        SetClass("has-primary", HasVisibleContent(PrimaryText));
        SetClass("has-secondary", HasVisibleContent(SecondaryText));
        SetClass("has-tertiary", HasVisibleContent(TertiaryText));
        SetClass("has-footer", HasVisibleContent(FooterText));
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
