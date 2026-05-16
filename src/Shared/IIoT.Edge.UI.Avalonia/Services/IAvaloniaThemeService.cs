using Avalonia.Styling;

namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaThemeService
{
    ThemeVariant CurrentTheme { get; }

    void Apply(ThemeVariant? themeVariant = null);
}
