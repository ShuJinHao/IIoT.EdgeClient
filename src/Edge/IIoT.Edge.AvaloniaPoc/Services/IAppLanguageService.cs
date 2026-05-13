namespace IIoT.Edge.AvaloniaPoc.Services;

public interface IAppLanguageService
{
    string CultureName { get; }

    string ToggleLabel { get; }

    string GetText(string key);

    void Apply(string cultureName);

    void Toggle();
}
