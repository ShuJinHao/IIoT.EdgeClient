using System.Text;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class ResourceEncodingHygieneTests
{
    private static readonly string[] CommonMojibakeFragments =
    [
        "\uFFFD",
        "锟",
        "Ã",
        "Â",
        "閿",
        "鐢熶骇",
        "杩愯",
        "妯″",
        "娴",
        "榛"
    ];

    [Fact]
    public void NavigationAvaloniaZhCnResources_ShouldUseReadableChineseText()
    {
        var root = FindRepositoryRoot();
        var resourceText = File.ReadAllText(GetNavigationResourcePath(root), Encoding.UTF8);

        Assert.Contains("[\"Navigation_Menu_Data\"] = \"生产数据\"", resourceText);
        Assert.Contains("[\"Navigation_Menu_Io\"] = \"I/O 交互\"", resourceText);
        Assert.Contains("[\"Navigation_Menu_CoreDiagnostics\"] = \"系统诊断\"", resourceText);
        Assert.Contains("[\"Navigation_Button_AddInteraction\"] = \"新增交互点\"", resourceText);
        Assert.Contains("[\"Navigation_Io_NoSignals\"] = \"当前设备没有可显示的 I/O 点位。\"", resourceText);
        Assert.Contains("[\"Navigation_Io_RuntimeNotStarted\"] = \"运行链路未启动，无法读取运行时快照。\"", resourceText);
        Assert.DoesNotContain("Demo", resourceText, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContainsMojibake(resourceText));
    }

    [Fact]
    public void AvaloniaMigrationTextAssets_ShouldNotContainCommonMojibakeFragments()
    {
        var root = FindRepositoryRoot();
        var files = GetTextAssetPaths(root).ToArray();
        var missingFiles = files
            .Where(path => !File.Exists(path))
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
        var findings = new List<string>();

        Assert.Empty(missingFiles);
        Assert.Contains(files, path => Path.GetFileName(path) == "NavigationAvaloniaResources.cs");
        Assert.Contains(files, path => Path.GetFileName(path) == "HomogenizationAvaloniaResources.cs");
        Assert.Contains(files, path => Path.GetFileName(path) == "launcher.profiles.json");
        Assert.Contains(files, path => Path.GetFileName(path) == "launcher.accounts.sample.json");

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

    private static bool ContainsMojibake(string value)
        => CommonMojibakeFragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));

    private static IEnumerable<string> GetTextAssetPaths(string root)
    {
        foreach (var path in Directory
                     .EnumerateFiles(Path.Combine(root, "src"), "*AvaloniaResources.cs", SearchOption.AllDirectories)
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

    private static string GetNavigationResourcePath(string root)
        => Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation.Avalonia",
            "Localization",
            "NavigationAvaloniaResources.cs");

    private static bool IsGeneratedPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(static segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("publish", StringComparison.OrdinalIgnoreCase));
    }
}
