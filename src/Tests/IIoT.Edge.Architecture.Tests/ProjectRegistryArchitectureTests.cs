using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IIoT.Edge.Architecture.Tests;

public sealed partial class ProjectRegistryArchitectureTests
{
    private static readonly string[] RequiredTestMetadata =
    [
        "TestKind",
        "TestRuntime",
        "TestRunnerMode",
        "TestCadence"
    ];

    [Fact]
    public void MainSolution_ShouldRegisterEveryPhysicalProjectExactlyOnce()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "IIoT.EdgeClient.slnx"));
        var registered = ProjectPathPattern()
            .Matches(solution)
            .Select(match => Normalize(match.Groups[1].Value))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var physical = EnumerateSourceFiles(root, "*.csproj")
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(registered.Distinct(StringComparer.OrdinalIgnoreCase).Count(), registered.Length);
        Assert.Equal(physical, registered, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestProjects_ShouldDeclareClassificationAndUsePhysicalCompilationOnly()
    {
        var root = FindRepositoryRoot();
        var testProjects = EnumerateSourceFiles(Path.Combine(root, "src", "Tests"), "*.csproj").ToArray();
        Assert.NotEmpty(testProjects);

        foreach (var path in testProjects)
        {
            var project = XDocument.Load(path);
            foreach (var property in RequiredTestMetadata)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(GetProjectProperty(project, property)),
                    $"{Normalize(Path.GetRelativePath(root, path))} must declare {property}.");
            }

            var linkedCompileItems = project.Descendants("Compile")
                .Where(item => item.Attribute("Link") is not null || item.Element("Link") is not null)
                .Select(item => item.Attribute("Include")?.Value ?? "<unknown>")
                .ToArray();
            Assert.Empty(linkedCompileItems);
        }
    }

    [Fact]
    public void TestSupportProjects_ShouldNotContainExecutableTestsOrProductionConsumers()
    {
        var root = FindRepositoryRoot();
        var supportRoot = Path.Combine(root, "src", "Testing");
        var supportProjects = EnumerateSourceFiles(supportRoot, "IIoT.Edge.Testing.*.csproj").ToArray();
        Assert.NotEmpty(supportProjects);

        var executableTests = supportProjects
            .Select(Path.GetDirectoryName)
            .Where(path => path is not null)
            .SelectMany(path => Directory.EnumerateFiles(path!, "*.cs", SearchOption.AllDirectories))
            .Where(path => !ShouldSkip(path))
            .Where(path => TestAttributePattern().IsMatch(File.ReadAllText(path)))
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .ToArray();
        Assert.Empty(executableTests);

        var productionReferences = EnumerateSourceFiles(root, "*.csproj")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Testing{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("IIoT.Edge.Testing.", StringComparison.Ordinal))
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .ToArray();
        Assert.Empty(productionReferences);
    }

    [Fact]
    public void LegacyBucketAndCompatibilityProjects_ShouldBePhysicallyAbsent()
    {
        var root = FindRepositoryRoot();
        var forbidden = new[]
        {
            "src/Tests/IIoT.Edge.NonUi" + "RegressionTests",
            "src/Tests/IIoT.Edge.Shell" + ".Tests",
            "src/Tests/IIoT.Edge.Module" + ".ContractTests",
            "src/Tests/IIoT.Edge.Infrastructure" + ".Update.Tests",
            "src/Tests/IIoT.Edge.Launcher" + ".Tests",
            "src/Tests/IIoT.Edge.Installer" + ".Tests",
            "src/Tests/IIoT.Edge.Golden" + "Tests"
        };

        Assert.DoesNotContain(
            forbidden,
            path => Directory.Exists(Path.Combine(root, NormalizeForOs(path))));
    }

    [Fact]
    public void RetiredProcessFamilyTokens_ShouldBeAbsentFromActiveWorkspace()
    {
        var root = FindRepositoryRoot();
        var retiredTokens = new[]
        {
            "Die" + "Cutting",
            "die" + "-cut",
            "模" + "切",
            "Polarity" + "Specific",
            "Shared" + "Die" + "Cutting"
        };
        var excludedTransitionFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scripts/tests/Test-EdgeRegressionLedger.ps1",
            "scripts/tests/baselines/edge-regression-ledger.json",
            "docs/改动复盘与规则沉淀.md"
        };
        var activeTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csproj", ".json", ".axaml", ".ps1", ".yml", ".yaml", ".props", ".targets", ".slnx", ".md"
        };
        var activeRoots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "scripts"),
            Path.Combine(root, ".github"),
            Path.Combine(root, "docs")
        };
        var rootFiles = new[]
        {
            Path.Combine(root, "IIoT.EdgeClient.slnx"),
            Path.Combine(root, "Directory.Build.props"),
            Path.Combine(root, "Directory.Build.targets"),
            Path.Combine(root, "Directory.Packages.props")
        };
        var findings = activeRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Concat(rootFiles.Where(File.Exists))
            .Where(path => !ShouldSkip(path))
            .Where(path => activeTextExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !excludedTransitionFiles.Contains(Normalize(Path.GetRelativePath(root, path))))
            .Where(path => retiredTokens.Any(token => File.ReadAllText(path).Contains(token, StringComparison.OrdinalIgnoreCase)))
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .ToArray();

        Assert.Empty(findings);
    }

    [GeneratedRegex("<Project\\s+Path=\"([^\"]+\\.csproj)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPathPattern();

    [GeneratedRegex(@"\[(?:Xunit\.)?(?:Fact|Theory)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TestAttributePattern();

    private static string? GetProjectProperty(XDocument project, string propertyName) =>
        project.Root?
            .Elements("PropertyGroup")
            .Select(group => group.Element(propertyName)?.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> EnumerateSourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Where(path => !ShouldSkip(path));

    private static bool ShouldSkip(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "publish" or ".git" or ".vs" or ".dotnet" or ".artifacts");
    }

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

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string NormalizeForOs(string path) => path.Replace('/', Path.DirectorySeparatorChar);
}
