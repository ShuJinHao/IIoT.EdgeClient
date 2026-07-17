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
        var evidenceGate = Path.Combine(root, "scripts", "tests", "Test-EdgeRetiredFeatureEvidence.ps1");
        var evidenceFixtures = Path.Combine(root, "scripts", "tests", "Test-EdgeRetiredFeatureEvidenceFixtures.ps1");
        Assert.True(File.Exists(evidenceGate), $"Missing retired feature evidence gate: {evidenceGate}");
        Assert.True(File.Exists(evidenceFixtures), $"Missing retired feature evidence fixtures: {evidenceFixtures}");

        var evidenceCommand = "run: ./scripts/tests/Test-EdgeRetiredFeatureEvidence.ps1 -RepositoryRoot .";
        var fixtureCommand = "run: ./scripts/tests/Test-EdgeRetiredFeatureEvidenceFixtures.ps1 -RepositoryRoot .";
        var workflows = new[]
        {
            Path.Combine(root, ".github", "workflows", "edge-smoke-build.yml"),
            Path.Combine(root, ".github", "workflows", "edge-pack-modules.yml")
        };
        foreach (var workflow in workflows)
        {
            Assert.True(File.Exists(workflow), $"Missing workflow: {workflow}");
            var workflowText = File.ReadAllText(workflow);
            Assert.Equal(1, workflowText.Split(evidenceCommand, StringSplitOptions.None).Length - 1);
            Assert.Equal(1, workflowText.Split(fixtureCommand, StringSplitOptions.None).Length - 1);

            var evidenceIndex = workflowText.IndexOf(evidenceCommand, StringComparison.Ordinal);
            var fixtureIndex = workflowText.IndexOf(fixtureCommand, StringComparison.Ordinal);
            var restoreIndex = workflowText.IndexOf("name: Restore ", StringComparison.Ordinal);
            Assert.True(evidenceIndex >= 0 && evidenceIndex < fixtureIndex,
                $"Retired feature evidence must run before its negative fixtures: {workflow}");
            Assert.True(fixtureIndex < restoreIndex,
                $"Retired feature evidence must remain in preflight before restore/package work: {workflow}");
        }

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
