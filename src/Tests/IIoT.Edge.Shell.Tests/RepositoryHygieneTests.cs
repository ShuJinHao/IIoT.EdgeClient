using System.Xml.Linq;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class RepositoryHygieneTests
{
    private static readonly string[] UpperLayerProjectFragments =
    [
        "Application",
        "Runtime",
        "Host",
        "Infrastructure",
        "Modules",
        "Presentation"
    ];

    private static readonly string[] ForbiddenContractNames =
    [
        "IIoT.Edge.Module." + "Abstractions",
        "IIoT.Edge.Module." + "Contracts",
        "IIoT.Edge.Integration." + "Contracts",
        "IIoT.Edge.Plugin." + "Shared"
    ];

    private static readonly string[] DeletedSdkArtifactNames =
    [
        "Module" + "Samples",
        "Dry" + "Run",
        "Scan" + "Capture" + "Starter",
        "Package" + "Validation" + "Client",
        "Loading" + "Scan" + "Task",
        "IIoT.Edge.Runtime." + "Scan",
        "Pack" + "Edge" + "Packages",
        "Run" + "Single" + "Repo" + "Release" + "Rehearsal",
        "New" + "Edge" + "Module",
        "New-" + "Edge" + "Module"
    ];

    private static readonly string[] MojibakeMarkers =
    [
        "\uFFFD"
    ];

    [Fact]
    public void SharedProjects_ShouldNotReferenceUpperLayers()
    {
        var root = FindRepositoryRoot();
        var uiSharedProject = Path.Combine(root, "src", "Shared", "IIoT.Edge.UI.Shared", "IIoT.Edge.UI.Shared.csproj");
        var sharedKernelProject = Path.Combine(root, "src", "Shared", "IIoT.Edge.SharedKernel", "IIoT.Edge.SharedKernel.csproj");

        var uiReferences = GetProjectReferences(uiSharedProject);
        Assert.All(uiReferences, reference => Assert.Contains("IIoT.Edge.SharedKernel", reference, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(uiReferences, reference =>
            UpperLayerProjectFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(GetProjectReferences(sharedKernelProject));
    }

    [Fact]
    public void MainSolution_ShouldNotContainToolsOrSdkSamples()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "IIoT.EdgeClient.slnx"));

        Assert.DoesNotContain("src/Tools", solution, StringComparison.OrdinalIgnoreCase);
        foreach (var deletedName in DeletedSdkArtifactNames)
        {
            Assert.DoesNotContain(deletedName, solution, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SourceTree_ShouldNotContainGeneratedOrDuplicateArtifacts()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");

        Assert.False(Directory.Exists(Path.Combine(root, ".codex-temp")), ".codex-temp 不应留在仓库根目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "%EDGE_SHARED_NUGET_FEED%")), "不应保留未展开环境变量形成的本地 NuGet 源目录。");
        Assert.False(File.Exists(Path.Combine(root, "IIoT.EdgeClient.DevTools.slnx")), "默认仓库根目录只保留主方案。");
        Assert.False(File.Exists(Path.Combine(root, "PACKAGE-README.md")), "不再保留 SDK/包化 README。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Pack" + "Edge" + "Packages.ps1")), "不再保留 NuGet 包化脚本。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Run" + "Single" + "Repo" + "Release" + "Rehearsal.ps1")), "不再保留包化发布演练脚本。");
        Assert.False(Directory.Exists(Path.Combine(root, "tools")), "根目录不再保留 tools 目录，正式脚本统一放入 scripts。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Tools")), "生产源码树不再保留 Tools 目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime.DataPipeline")), "不再保留旧 DataPipeline 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime." + "Scan")), "不再保留旧 Scan 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime", "Stations")), "Runtime 不再保留旧站点示例目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Excel")), "不再保留未接入主方案的 Excel 空壳目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.DataMapping")), "不再保留未接入主方案的 DataMapping 空壳目录。");

        var nugetConfig = File.ReadAllText(Path.Combine(root, "NuGet.Config"));
        Assert.DoesNotContain("%EDGE_SHARED_NUGET_FEED%", nugetConfig, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".artifacts", nugetConfig, StringComparison.OrdinalIgnoreCase);

        var wpftmpProjects = EnumerateFiles(sourceRoot, "*_wpftmp.csproj")
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
        Assert.Empty(wpftmpProjects);

        var fontFiles = EnumerateFiles(sourceRoot, "*.*")
            .Where(IsFontFile)
            .Select(path => ToRepositoryPath(root, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            ["src/Shared/IIoT.Edge.UI.Shared/Assets/fonts/iconfont.ttf"],
            fontFiles);

        var duplicateFontDirectories = Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldSkip(path))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return string.Equals(name, "Noto", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Roboto", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
        Assert.Empty(duplicateFontDirectories);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceOldContractProjects()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, ForbiddenContractNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceDeletedSdkArtifacts()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, DeletedSdkArtifactNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SourceTree_ShouldNotContainMojibakeMarkers()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, MojibakeMarkers))
            .ToArray();

        Assert.Empty(matches);
    }

    private static IReadOnlyList<string> GetProjectReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    private static IEnumerable<string> FindForbiddenMatches(string root, string path, IReadOnlyList<string> forbiddenNames)
    {
        var text = File.ReadAllText(path);
        foreach (var forbiddenName in forbiddenNames)
        {
            if (text.Contains(forbiddenName, StringComparison.Ordinal))
            {
                yield return $"{ToRepositoryPath(root, path)} contains {forbiddenName}";
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !ShouldSkip(path));

    private static bool IsFontFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextCandidate(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, "CODEOWNERS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkip(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".dotnet", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 IIoT.EdgeClient 仓库根目录。");
    }

    private static string ToRepositoryPath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');
}
