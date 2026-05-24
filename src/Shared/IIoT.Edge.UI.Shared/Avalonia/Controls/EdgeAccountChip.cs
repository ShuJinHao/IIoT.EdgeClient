using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeAccountChip : Button
{
    public static readonly StyledProperty<string?> DisplayNameProperty =
        AvaloniaProperty.Register<EdgeAccountChip, string?>(nameof(DisplayName));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<EdgeAccountChip, string?>(nameof(Subtitle));

    public string? DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
