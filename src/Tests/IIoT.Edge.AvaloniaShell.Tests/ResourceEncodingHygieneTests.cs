using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class ResourceEncodingHygieneTests
{
    private static readonly string[] CommonMojibakeFragments =
    [
        "\uFFFD",
        "Ã",
        "Â",
        "锟",
        "閿",
        "闁",
        "鏉",
        "濡"
    ];

    [Fact]
    public void Unified_xaml_resources_should_use_readable_chinese_text()
    {
        var root = FindRepositoryRoot();

        Assert.Contains(
            "<sys:String x:Key=\"Shell_Login\">登录</sys:String>",
            File.ReadAllText(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Shell.Avalonia", "zh-CN"), Encoding.UTF8));
        Assert.Contains(
            "<sys:String x:Key=\"Navigation_Menu_Data\">生产数据</sys:String>",
            File.ReadAllText(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Navigation.Avalonia", "zh-CN"), Encoding.UTF8));
        Assert.Contains(
            "<sys:String x:Key=\"Navigation_Menu_Io\">I/O 交互</sys:String>",
            File.ReadAllText(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Navigation.Avalonia", "zh-CN"), Encoding.UTF8));
        Assert.Contains(
            "<sys:String x:Key=\"Homogenization_Title_Data\">匀浆出料数据</sys:String>",
            File.ReadAllText(GetLanguagePath(root, "Modules", "IIoT.Edge.Module.Homogenization", "zh-CN"), Encoding.UTF8));
        Assert.Contains(
            "<sys:String x:Key=\"Panels_Tab_HardwareStatus\">硬件状态</sys:String>",
            File.ReadAllText(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Panels.Avalonia", "zh-CN"), Encoding.UTF8));
    }

    [Fact]
    public void AvaloniaMigration_text_assets_should_not_contain_common_mojibake_fragments()
    {
        var root = FindRepositoryRoot();
        var files = GetTextAssetPaths(root).ToArray();
        var findings = new List<string>();

        Assert.NotEmpty(files);
        Assert.All(files, path => Assert.True(File.Exists(path), ToRepositoryPath(root, path)));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.UTF8);

            foreach (var fragment in CommonMojibakeFragments)
            {
                if (text.Contains(fragment, StringComparison.Ordinal))
                {
                    findings.Add($"{ToRepositoryPath(root, file)} contains {ToCodePoints(fragment)}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void Avalonia_resources_should_use_xaml_language_dictionaries_instead_of_code_tables()
    {
        var root = FindRepositoryRoot();
        var codeTables = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*AvaloniaResources.cs", SearchOption.AllDirectories)
            .Where(static path => !IsGeneratedPath(path))
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();

        Assert.Empty(codeTables);
        Assert.True(File.Exists(GetLanguagePath(root, "Edge", "IIoT.Edge.AvaloniaShell", "zh-CN")));
        Assert.True(File.Exists(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Shell.Avalonia", "zh-CN")));
        Assert.True(File.Exists(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Navigation.Avalonia", "zh-CN")));
        Assert.True(File.Exists(GetLanguagePath(root, "Presentation", "IIoT.Edge.Presentation.Panels.Avalonia", "zh-CN")));
        Assert.True(File.Exists(GetLanguagePath(root, "Modules", "IIoT.Edge.Module.Homogenization", "zh-CN")));
    }

    [Fact]
    public void Avalonia_theme_resources_should_be_present_and_wired_from_app_xaml()
    {
        var root = FindRepositoryRoot();
        var sharedThemeRoot = Path.Combine(root, "src", "Shared", "IIoT.Edge.UI.Avalonia", "Themes");
        var launcherThemeRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "Themes");

        Assert.True(File.Exists(Path.Combine(sharedThemeRoot, "IndustrialTheme.axaml")));
        Assert.True(File.Exists(Path.Combine(sharedThemeRoot, "AppTypography.axaml")));
        Assert.True(File.Exists(Path.Combine(launcherThemeRoot, "LauncherTheme.axaml")));

        var shellApp = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.AvaloniaShell", "App.axaml"), Encoding.UTF8);
        var launcherApp = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "App.axaml"), Encoding.UTF8);

        Assert.DoesNotContain("RequestedThemeVariant", shellApp, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedThemeVariant", launcherApp, StringComparison.Ordinal);
        Assert.Contains("IndustrialTheme.axaml", shellApp, StringComparison.Ordinal);
        Assert.Contains("AppTypography.axaml", shellApp, StringComparison.Ordinal);
        Assert.Contains("LauncherTheme.axaml", launcherApp, StringComparison.Ordinal);
        Assert.Contains("AppTypography.axaml", launcherApp, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_design_system_should_define_phase7_tokens_and_classes()
    {
        var root = FindRepositoryRoot();
        var sharedThemeRoot = Path.Combine(root, "src", "Shared", "IIoT.Edge.UI.Avalonia", "Themes");
        var launcherThemeRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "Themes");

        var industrialThemePath = Path.Combine(sharedThemeRoot, "IndustrialTheme.axaml");
        var typographyPath = Path.Combine(sharedThemeRoot, "AppTypography.axaml");
        var launcherThemePath = Path.Combine(launcherThemeRoot, "LauncherTheme.axaml");

        var industrialTheme = File.ReadAllText(industrialThemePath, Encoding.UTF8);
        var typography = File.ReadAllText(typographyPath, Encoding.UTF8);
        var launcherTheme = File.ReadAllText(launcherThemePath, Encoding.UTF8);

        AssertContainsAll(
            industrialTheme,
            industrialThemePath,
            "x:Key=\"Edge.Status.Neutral\"",
            "x:Key=\"Edge.Status.Muted\"",
            "x:Key=\"Edge.Status.Running\"",
            "x:Key=\"Edge.Status.Stopped\"",
            "x:Key=\"Edge.Status.Failed\"",
            "x:Key=\"Edge.Status.Development\"",
            "x:Key=\"Edge.Shadow.Card\"",
            "Selector=\"Border.edge-card\"",
            "Selector=\"Border.edge-kpi-card\"",
            "Selector=\"Border.edge-status-card\"",
            "Selector=\"Border.edge-status-card.running\"",
            "Selector=\"Border.edge-table-card\"",
            "Selector=\"Border.edge-form-section\"",
            "Selector=\"Border.edge-empty-state\"",
            "Selector=\"Border.edge-dialog-card\"",
            "Selector=\"Border.edge-log-entry\"",
            "Selector=\"Border.edge-status-pill\"",
            "Selector=\"Button.danger\"",
            "Selector=\"ListBox.edge-log-list\"",
            "Selector=\"TabControl.edge-tool-tabs\"",
            "Selector=\"DataGrid\"");

        AssertContainsAll(
            typography,
            typographyPath,
            "x:Key=\"App.FontSize.Micro\"",
            "x:Key=\"App.FontSize.Metric\"",
            "x:Key=\"App.FontSize.Display\"",
            "Selector=\"TextBlock.app-page-title\"",
            "Selector=\"TextBlock.app-section-title\"",
            "Selector=\"TextBlock.app-caption\"");

        AssertContainsAll(
            launcherTheme,
            launcherThemePath,
            "x:Key=\"Launcher.Status.Neutral\"",
            "x:Key=\"Launcher.Status.Muted\"",
            "x:Key=\"Launcher.Status.Running\"",
            "x:Key=\"Launcher.Status.Stopped\"",
            "x:Key=\"Launcher.Status.Failed\"",
            "x:Key=\"Launcher.Status.Development\"",
            "Selector=\"Border.launcher-status-card.running\"",
            "Selector=\"Border.launcher-status-card.failed\"",
            "Selector=\"Border.launcher-status-card.development\"");
    }

    [Fact]
    public void Avalonia_phase7_design_system_docs_should_exist_and_match_index()
    {
        var root = FindRepositoryRoot();
        var designSystemPath = Path.Combine(root, "docs", "Avalonia-Industrial-Design-System.md");
        var checklistPath = Path.Combine(root, "docs", "Avalonia-UI-验收清单.md");
        var indexPath = Path.Combine(root, "docs", "avalonia-ui-refactor-plan", "00_INDEX.md");
        var forbiddenFragments = new[] { "鎬", "€", "\uE178", "鍖", "涓" };

        Assert.True(File.Exists(designSystemPath), ToRepositoryPath(root, designSystemPath));
        Assert.True(File.Exists(checklistPath), ToRepositoryPath(root, checklistPath));
        Assert.True(File.Exists(indexPath), ToRepositoryPath(root, indexPath));

        var designSystem = File.ReadAllText(designSystemPath, Encoding.UTF8);
        var checklist = File.ReadAllText(checklistPath, Encoding.UTF8);
        var index = File.ReadAllText(indexPath, Encoding.UTF8);

        AssertContainsAll(
            designSystem,
            designSystemPath,
            "Edge.*",
            "Ind.*",
            "Launcher.*",
            "真实数据底线",
            "禁止事项",
            "状态语义");

        AssertContainsAll(
            checklist,
            checklistPath,
            "1366x768",
            "1600x1000",
            "1900x1200",
            "不造假",
            "真实数据",
            "人工验收");

        AssertContainsAll(
            index,
            indexPath,
            "Phase 7 主题与设计系统固化",
            "本批已执行，待评审",
            "Avalonia-Industrial-Design-System.md",
            "Avalonia-UI-验收清单.md");

        foreach (var file in new[] { designSystemPath, checklistPath, indexPath })
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            foreach (var fragment in forbiddenFragments)
            {
                Assert.DoesNotContain(fragment, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Avalonia_fonts_should_come_from_xaml_resources()
    {
        var root = FindRepositoryRoot();
        var typography = File.ReadAllText(
            Path.Combine(root, "src", "Shared", "IIoT.Edge.UI.Avalonia", "Themes", "AppTypography.axaml"),
            Encoding.UTF8);
        var shellProgram = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.AvaloniaShell", "Program.cs"), Encoding.UTF8);
        var launcherProgram = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia", "Program.cs"), Encoding.UTF8);

        Assert.Contains("App.FontFamily.Default", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("WithInterFont", shellProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("WithInterFont", launcherProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_ui_sources_should_not_inline_hex_colors()
    {
        var root = FindRepositoryRoot();
        var findings = GetProductionSourceFiles(root)
            .Where(static path => !IsResourceDefinitionPath(path))
            .Select(path => new
            {
                Path = ToRepositoryPath(root, path),
                Matches = Regex.Matches(
                        File.ReadAllText(path, Encoding.UTF8),
                        @"#[0-9A-Fa-f]{6,8}")
                    .Select(match => match.Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item => item.Matches.Length > 0)
            .Select(item => $"{item.Path}: {string.Join(", ", item.Matches)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void Xaml_language_dictionaries_should_have_matching_zh_and_en_keys()
    {
        var root = FindRepositoryRoot();
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(root, "src"), "Resources", SearchOption.AllDirectories)
                     .Select(path => Path.Combine(path, "Languages"))
                     .Where(Directory.Exists))
        {
            var zh = ReadKeys(Path.Combine(directory, "zh-CN.xaml"));
            var en = ReadKeys(Path.Combine(directory, "en-US.xaml"));

            Assert.NotEmpty(zh);
            Assert.Equal(zh.Order(), en.Order());
        }
    }

    [Fact]
    public void Avalonia_resource_references_should_resolve_from_unified_xaml_dictionaries()
    {
        var root = FindRepositoryRoot();
        var resourceKeys = GetXamlResourceDefinitionFiles(root)
            .Where(static path => !IsGeneratedPath(path))
            .SelectMany(ReadKeys)
            .ToHashSet(StringComparer.Ordinal);

        var unresolved = GetProductionSourceFiles(root)
            .SelectMany(path => FindResourceReferences(File.ReadAllText(path, Encoding.UTF8))
                .Select(key => new
                {
                    Key = key,
                    Path = ToRepositoryPath(root, path)
                }))
            .Where(item => !resourceKeys.Contains(item.Key))
            .Select(item => $"{item.Path}: {item.Key}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unresolved);
    }

    private static IEnumerable<string> GetTextAssetPaths(string root)
    {
        foreach (var path in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories)
                     .Where(static path => !IsGeneratedPath(path))
                     .Order(StringComparer.Ordinal))
        {
            yield return path;
        }

        var launcherRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher.Avalonia");
        yield return Path.Combine(launcherRoot, "launcher.profiles.json");
        yield return Path.Combine(launcherRoot, "launcher.accounts.sample.json");

        foreach (var path in Directory
                     .EnumerateFiles(Path.Combine(root, "docs"), "Avalonia12-*.md", SearchOption.TopDirectoryOnly)
                     .Where(static path => !Path.GetFileName(path).Contains("审核", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            yield return path;
        }

        yield return Path.Combine(root, "docs", "avalonia-ui-refactor-plan", "00_INDEX.md");
        yield return Path.Combine(root, "docs", "Avalonia-Industrial-Design-System.md");
        yield return Path.Combine(root, "docs", "Avalonia-UI-验收清单.md");
    }

    private static void AssertContainsAll(string text, string path, params string[] expectedFragments)
    {
        foreach (var fragment in expectedFragments)
        {
            Assert.True(text.Contains(fragment, StringComparison.Ordinal), $"{path} missing {fragment}");
        }
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

    private static IEnumerable<string> GetXamlResourceDefinitionFiles(string root)
    {
        foreach (var path in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories)
                     .Where(IsResourceDefinitionPath))
        {
            yield return path;
        }

        foreach (var path in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*.axaml", SearchOption.AllDirectories)
                     .Where(IsResourceDefinitionPath))
        {
            yield return path;
        }
    }

    private static IEnumerable<string> GetProductionSourceFiles(string root)
        => Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                  || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                                  || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !IsGeneratedPath(path))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> FindResourceReferences(string text)
    {
        foreach (Match match in Regex.Matches(text, @"DynamicResource\s+([A-Za-z0-9_.]+)"))
        {
            yield return match.Groups[1].Value;
        }

        foreach (Match match in Regex.Matches(text, @"GetText\(\s*""([A-Za-z0-9_]+)"""))
        {
            yield return match.Groups[1].Value;
        }

        foreach (Match match in Regex.Matches(text, @"HomogenizationText\.(?:Get|Format)\(\s*""([A-Za-z0-9_]+)"""))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static string GetLanguagePath(string root, string area, string projectName, string culture)
    {
        var projectRoot = area switch
        {
            "Edge" => Path.Combine(root, "src", "Edge", projectName),
            "Presentation" => Path.Combine(root, "src", "Presentation", projectName),
            "Modules" => Path.Combine(root, "src", "Modules", projectName),
            _ => throw new ArgumentOutOfRangeException(nameof(area), area, null)
        };

        return Path.Combine(projectRoot, "Resources", "Languages", $"{culture}.xaml");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Cannot locate IIoT.EdgeClient.AvaloniaMigration repository root.");
    }

    private static string ToRepositoryPath(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string ToCodePoints(string value)
        => string.Join(" ", value.Select(character => $"U+{(int)character:X4}"));

    private static bool IsResourceDefinitionPath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}Languages{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || path.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneratedPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("publish", StringComparison.OrdinalIgnoreCase));
    }
}
