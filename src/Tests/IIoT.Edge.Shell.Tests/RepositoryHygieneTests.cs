using System.Text.RegularExpressions;
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

    private static readonly string[] DeletedOverWrappedApiNames =
    [
        "Plugin" + "Cloud" + "Upload" + "Mode",
        "Plugin" + "Mes" + "Upload" + "Mode",
        "Plugin" + "Upload" + "Modes",
        "I" + "Edge" + "Module",
        "I" + "Module" + "Loader"
    ];

    private static readonly string[] MojibakeMarkers =
    [
        "\uFFFD",
        "\u6D93\u5D85",
        "\u6D60\u64B3",
        "\u93CD\u572D",
        "\u6D30\u8930",
        "\u6DC7\u6FE7",
        "\u93C8\uE061",
        "\u9359\u6A58",
        "\u9356\uE15C",
        "\u8FBE\u64B3",
        "\u93C3\u72B3",
        "\u7039\u6C56",
        "\uE15C",
        "\u20AC?"
    ];

    private static readonly Regex LongTaskDelayPattern = new(
        @"Task\.Delay\(\s*(?:1\d{2,}|\d{4,}|TimeSpan\.FromMilliseconds\(\s*(?:1\d{2,}|\d{4,}))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DirectVisibleValidationIssuePattern = new(
        @"new\s+ValidationIssue\s*\(\s*""[^""]*[\u4e00-\u9fff]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ResourceLookupPattern = new(
        @"(?:GetText|FormatText)\(\s*""([^""]+)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        Assert.False(File.Exists(Path.Combine(root, "IIoT.EdgeClient.DevTools.slnx")), "仓库根目录只保留主方案。");
        Assert.False(File.Exists(Path.Combine(root, "PACKAGE-README.md")), "不再保留 SDK 或包化 README。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Pack" + "Edge" + "Packages.ps1")), "不再保留 NuGet 包化脚本。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Run" + "Single" + "Repo" + "Release" + "Rehearsal.ps1")), "不再保留包化发布演练脚本。");
        Assert.False(Directory.Exists(Path.Combine(root, "tools")), "根目录不再保留 tools 目录，正式脚本统一放入 scripts。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Tools")), "生产源码树不再保留 Tools 目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime.DataPipeline")), "不再保留旧 DataPipeline 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime." + "Scan")), "不再保留旧 Scan 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime", "Stations")), "Runtime 不再保留旧站点示例目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Excel")), "不再保留未接入主方案的 Excel 空壳目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.DataMapping")), "不再保留未接入主方案的 DataMapping 空壳目录。");
        Assert.False(File.Exists(Path.Combine(root, "src", "Core", "domain_restore.txt")), "不应保留 dotnet restore 输出日志。");
        Assert.False(File.Exists(Path.Combine(root, "src", "Infrastructure", "full_restore_output_en.txt")), "不应保留 dotnet restore 输出日志。");

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
    public void ShellAppSettings_ShouldNotContainCommittedLicenseOrJwtSecrets()
    {
        var root = FindRepositoryRoot();
        var appsettingsPath = Path.Combine(root, "src", "Edge", "IIoT.Edge.Shell", "appsettings.json");
        var appsettings = File.ReadAllText(appsettingsPath);

        Assert.DoesNotContain("\"LicenseKey\"", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediatR", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant), appsettings);
    }

    [Fact]
    public void IntegrationDependencyInjection_ShouldNotCacheTypedHttpClientsAsSingletons()
    {
        var root = FindRepositoryRoot();
        var dependencyInjection = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "DependencyInjection.cs"));

        Assert.DoesNotContain("AddHttpClient<AuthService>", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHttpClient<DeviceService>", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<AuthService>", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient(AuthService.HttpClientName", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient(DeviceService.HttpClientName", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void EfCoreSqliteConnection_ShouldEnableWalMode()
    {
        var root = FindRepositoryRoot();
        var sqliteConnection = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Persistence.EfCore",
            "EdgeSqliteConnection.cs"));

        Assert.Contains("PRAGMA journal_mode=WAL;", sqliteConnection, StringComparison.Ordinal);
        Assert.Contains("BusyTimeoutMilliseconds = 5000", sqliteConnection, StringComparison.Ordinal);
        Assert.Contains("PRAGMA busy_timeout={BusyTimeoutMilliseconds};", sqliteConnection, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=edge_design.db", sqliteConnection, StringComparison.OrdinalIgnoreCase);
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
    public void SourceTree_ShouldNotReferenceDeletedOverWrappedApis()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, DeletedOverWrappedApiNames))
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

    [Fact]
    public void ApplicationAbstractions_ShouldNotContainImplementationHelpers()
    {
        var root = FindRepositoryRoot();
        var abstractionsRoot = Path.Combine(root, "src", "Application", "IIoT.Edge.Application", "Abstractions");
        var forbiddenPatterns = new[]
        {
            new Regex(@"\b(static|internal\s+static|public\s+static)\s+class\b", RegexOptions.CultureInvariant),
            new Regex(@"\bclass\s+\w*Helper\b", RegexOptions.CultureInvariant),
            new Regex(@"\b(File|Directory)\.", RegexOptions.CultureInvariant),
            new Regex(@"\bSHA256\b", RegexOptions.CultureInvariant),
            new Regex(@"\bTask\.Delay\b", RegexOptions.CultureInvariant)
        };

        var matches = EnumerateFiles(abstractionsRoot, "*.cs")
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => pattern.IsMatch(text))
                    .Select(pattern => $"{ToRepositoryPath(root, path)} contains implementation detail pattern {pattern}");
            })
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void NavigationLanguageDictionaries_ShouldHaveSameResourceKeys()
    {
        var root = FindRepositoryRoot();
        var languageRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Resources",
            "Languages");

        var zhKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.xaml"));
        var enKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.xaml"));

        Assert.Empty(zhKeys.Except(enKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Empty(enKeys.Except(zhKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NavigationFeatureResourceLookups_ShouldExistInLanguageDictionaries()
    {
        var root = FindRepositoryRoot();
        var navigationRoot = Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Navigation");
        var languageRoot = Path.Combine(navigationRoot, "Resources", "Languages");
        var dictionaryKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.xaml"))
            .Union(GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.xaml")), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var missingKeys = EnumerateFiles(Path.Combine(navigationRoot, "Features"), "*.cs")
            .SelectMany(path => ResourceLookupPattern
                .Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Where(key => !dictionaryKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    [Fact]
    public void NavigationFeatures_ShouldNotCreateVisibleChineseValidationIssuesDirectly()
    {
        var root = FindRepositoryRoot();
        var featureRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features");

        var matches = EnumerateFiles(featureRoot, "*.cs")
            .SelectMany(path => DirectVisibleValidationIssuePattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} contains direct visible validation text at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Tests_ShouldNotUseLongFixedTaskDelaysForSynchronization()
    {
        var root = FindRepositoryRoot();
        var testRoot = Path.Combine(root, "src", "Tests");

        var matches = EnumerateFiles(testRoot, "*.cs")
            .SelectMany(path => LongTaskDelayPattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} contains long fixed Task.Delay at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ShellVisibleXaml_ShouldUseDynamicResourcesForChineseText()
    {
        var root = FindRepositoryRoot();
        var xamlRoots = new[]
        {
            Path.Combine(root, "src", "Edge", "IIoT.Edge.Shell"),
            Path.Combine(root, "src", "Presentation"),
            Path.Combine(root, "src", "Modules")
        };

        var matches = xamlRoots
            .Where(Directory.Exists)
            .SelectMany(path => EnumerateFiles(path, "*.xaml"))
            .Where(path => !ToRepositoryPath(root, path).Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase))
            .Where(ContainsChineseText)
            .Select(path => ToRepositoryPath(root, path))
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

    private static bool ContainsChineseText(string path)
        => File.ReadAllText(path).Any(ch => ch >= '\u4e00' && ch <= '\u9fff');

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
