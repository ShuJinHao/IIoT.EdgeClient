using Avalonia;
using Avalonia.Styling;

namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaThemeService : IAvaloniaThemeService
{
    public ThemeVariant CurrentTheme { get; private set; } = ThemeVariant.Light;

    public void Apply(ThemeVariant? themeVariant = null)
    {
        CurrentTheme = themeVariant ?? ThemeVariant.Light;
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = CurrentTheme;
        }
    }
}
