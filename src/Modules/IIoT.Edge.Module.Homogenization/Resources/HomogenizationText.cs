using System.Globalization;
using System.Collections.Concurrent;
using System.Xml.Linq;
using Avalonia.Platform;

namespace IIoT.Edge.Module.Homogenization.Resources;

internal static class HomogenizationText
{
    private const string ResourceAssemblyName = "IIoT.Edge.Module.Homogenization";
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> LanguageCache = new(StringComparer.OrdinalIgnoreCase);

    public static string Get(string key, string fallback)
    {
        var app = Avalonia.Application.Current;
        if (app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var cultureName = CultureInfo.CurrentUICulture.Name;
        if (TryGetFromLanguageDictionary(cultureName, key, out var localized))
        {
            return localized;
        }

        if (!string.Equals(cultureName, "zh-CN", StringComparison.OrdinalIgnoreCase)
            && TryGetFromLanguageDictionary("zh-CN", key, out localized))
        {
            return localized;
        }

        return fallback;
    }

    public static string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);

    private static bool TryGetFromLanguageDictionary(string cultureName, string key, out string value)
    {
        value = string.Empty;
        var dictionary = LanguageCache.GetOrAdd(cultureName, LoadLanguageDictionary);
        return dictionary.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);
    }

    private static IReadOnlyDictionary<string, string> LoadLanguageDictionary(string cultureName)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(HomogenizationText).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            var filePath = Path.Combine(assemblyDirectory, "Resources", "Languages", $"{cultureName}.axaml");
            if (File.Exists(filePath))
            {
                return LoadLanguageDictionaryFile(filePath);
            }
        }

        var source = new Uri($"avares://{ResourceAssemblyName}/Resources/Languages/{cultureName}.axaml");
        if (!AssetLoader.Exists(source))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = AssetLoader.Open(source);
        return LoadLanguageDictionaryDocument(XDocument.Load(stream));
    }

    private static IReadOnlyDictionary<string, string> LoadLanguageDictionaryFile(string filePath)
        => LoadLanguageDictionaryDocument(XDocument.Load(filePath));

    private static IReadOnlyDictionary<string, string> LoadLanguageDictionaryDocument(XDocument document)
    {
        var keyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

        return document
            .Descendants()
            .Select(element => new
            {
                Key = element.Attribute(keyName)?.Value,
                Value = element.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key!, item => item.Value, StringComparer.Ordinal);
    }
}
