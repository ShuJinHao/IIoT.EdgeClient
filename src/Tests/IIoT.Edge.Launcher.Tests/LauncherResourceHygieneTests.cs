using System.Text;
using System.Xml.Linq;
using IIoT.Edge.Launcher;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherResourceHygieneTests
{
    private static readonly string[] MojibakeFragments =
    [
        "\uFFFD",
        "鏈",
        "璐",
        "瀵",
        "鐧",
        "淇",
        "鎼"
    ];

    [Fact]
    public void Launcher_language_dictionaries_should_have_matching_keys()
    {
        var root = FindRepositoryRoot();
        var languageRoot = Path.Combine(
            root,
            "src",
            "Edge",
            "IIoT.Edge.Launcher.Avalonia",
            "Resources",
            "Languages");

        var zhKeys = ReadKeys(Path.Combine(languageRoot, "zh-CN.xaml"));
        var enKeys = ReadKeys(Path.Combine(languageRoot, "en-US.xaml"));

        Assert.Contains("Launcher_Login_Title", zhKeys);
        Assert.Equal(zhKeys.Order(), enKeys.Order());
    }

    [Fact]
    public void Launcher_views_and_resources_should_not_contain_known_mojibake_fragments()
    {
        var root = FindRepositoryRoot();
        var launcherRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia");
        var files = Directory
            .EnumerateFiles(Path.Combine(launcherRoot, "Views"), "*.axaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(launcherRoot, "Views"), "*.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(Path.Combine(launcherRoot, "ViewModels"), "*.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(Path.Combine(launcherRoot, "Resources", "Languages"), "*.xaml", SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var findings = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            foreach (var fragment in MojibakeFragments)
            {
                if (text.Contains(fragment, StringComparison.Ordinal))
                {
                    findings.Add($"{Path.GetRelativePath(root, file)} contains {fragment}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void AddLauncherServices_should_resolve_language_service()
    {
        using var provider = new ServiceCollection()
            .AddLauncherServices(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .BuildServiceProvider();

        var languageService = provider.GetRequiredService<IAvaloniaLanguageService>();
        languageService.Apply("zh-CN");

        Assert.Equal("本地登录", languageService.GetText("Launcher_Login_Title"));
    }

    private static IReadOnlyList<string> ReadKeys(string path)
    {
        Assert.True(File.Exists(path), path);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Descendants()
            .Select(element => element.Attribute(xaml + "Key")?.Value)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key!)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate IIoT.EdgeClient.AvaloniaMigration repository root.");
    }
}
