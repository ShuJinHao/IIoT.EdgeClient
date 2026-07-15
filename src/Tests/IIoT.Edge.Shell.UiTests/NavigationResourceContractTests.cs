using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IIoT.Edge.Shell.UiTests;

public sealed partial class NavigationResourceContractTests
{
    [Fact]
    public void LanguageDictionaries_ShouldExposeTheSameResourceKeys()
    {
        var languageRoot = GetLanguageRoot();
        var zhKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"));
        var enKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml"));

        Assert.Empty(zhKeys.Except(enKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Empty(enKeys.Except(zhKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void LanguageDictionaries_ShouldNotKeepHostProcessDisplayKeys()
    {
        var languageRoot = GetLanguageRoot();
        var processKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"))
            .Union(GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml")), StringComparer.Ordinal)
            .Where(key => key.StartsWith("Navigation_Process_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(processKeys);
    }

    [Fact]
    public void FeatureResourceLookups_ShouldExistInLanguageDictionaries()
    {
        var root = FindRepositoryRoot();
        var navigationRoot = Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Navigation");
        var languageRoot = Path.Combine(navigationRoot, "Resources", "Languages");
        var dictionaryKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"))
            .Union(GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml")), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var missingKeys = Directory.EnumerateFiles(Path.Combine(navigationRoot, "Features"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => ResourceLookupPattern()
                .Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Where(key => !dictionaryKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    [GeneratedRegex("(?:GetText|FormatText)\\(\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceLookupPattern();

    private static IReadOnlySet<string> GetXamlResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetLanguageRoot() => Path.Combine(
        FindRepositoryRoot(),
        "src",
        "Presentation",
        "IIoT.Edge.Presentation.Navigation",
        "Resources",
        "Languages");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the IIoT.EdgeClient repository root.");
    }
}
