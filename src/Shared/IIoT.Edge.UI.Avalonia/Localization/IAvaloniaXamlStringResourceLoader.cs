using System.Reflection;

namespace IIoT.Edge.UI.Avalonia.Localization;

public interface IAvaloniaXamlStringResourceLoader
{
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(
        IEnumerable<Assembly> assemblies,
        IReadOnlyCollection<string>? cultureNames = null);
}
