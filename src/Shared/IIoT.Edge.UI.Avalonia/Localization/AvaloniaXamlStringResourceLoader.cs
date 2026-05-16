using System.Reflection;
using System.Xml.Linq;

namespace IIoT.Edge.UI.Avalonia.Localization;

public sealed class AvaloniaXamlStringResourceLoader : IAvaloniaXamlStringResourceLoader
{
    private static readonly string[] DefaultCultureNames = ["zh-CN", "en-US"];
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load(
        IEnumerable<Assembly> assemblies,
        IReadOnlyCollection<string>? cultureNames = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var cultures = cultureNames is { Count: > 0 } ? cultureNames : DefaultCultureNames;
        var result = cultures.ToDictionary(
            static culture => culture,
            static _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies.Where(static assembly => assembly is not null).Distinct())
        {
            foreach (var culture in cultures)
            {
                var resourceName = FindResourceName(assembly, culture);
                if (resourceName is null)
                {
                    continue;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                foreach (var pair in ReadStringResources(stream))
                {
                    result[culture][pair.Key] = pair.Value;
                }
            }
        }

        return result.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyDictionary<string, string>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindResourceName(Assembly assembly, string culture)
    {
        var suffix = $".Resources.Languages.{culture}.xaml";
        return assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<KeyValuePair<string, string>> ReadStringResources(Stream stream)
    {
        var document = XDocument.Load(stream, LoadOptions.None);
        foreach (var element in document.Descendants())
        {
            var key = element.Attribute(XamlNamespace + "Key")?.Value;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(key, element.Value);
        }
    }
}
