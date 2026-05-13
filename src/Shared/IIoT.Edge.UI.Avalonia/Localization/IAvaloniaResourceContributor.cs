namespace IIoT.Edge.UI.Avalonia.Localization;

public interface IAvaloniaResourceContributor
{
    string CultureName { get; }

    IReadOnlyDictionary<string, string> GetResources();
}
