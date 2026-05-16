namespace IIoT.Edge.UI.Avalonia.Localization;

public interface IAvaloniaLanguageService
{
    string CultureName { get; }

    string ToggleLabel { get; }

    event EventHandler? LanguageChanged;

    string GetText(string key);

    void Apply(string cultureName);

    void Toggle();
}
