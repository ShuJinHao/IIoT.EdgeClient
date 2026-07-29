using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace IIoT.Edge.Architecture.Tests;

public sealed partial class ProjectRegistryArchitectureTests
{
    private static readonly string[] RequiredTestMetadata =
    [
        "TestKind",
        "TestRuntime",
        "TestRuntimeDependencies",
        "TestRunnerMode",
        "TestCapability",
        "TestRisk",
        "TestConcern",
        "TestProfile",
        "TestOwner",
        "TestRuleId"
    ];

    private static readonly HashSet<string> AllowedTestKinds =
    [
        "Aggregate",
        "Application",
        "Architecture",
        "Conformance",
        "Contract",
        "Deployment",
        "Integration",
        "Persistence",
        "UI",
        "Unit",
        "Workflow"
    ];

    private static readonly HashSet<string> AllowedTestRuntimes =
    [
        "Pure",
        "Filesystem",
        "Network",
        "Avalonia",
        "SQLite",
        "Windows"
    ];

    private static readonly HashSet<string> AllowedRuntimeDependencies =
    [
        "AssemblyLoad",
        "ControlledConcurrency",
        "FakeHttp",
        "FakeTime",
        "Filesystem",
        "Headless",
        "IsolatedDatabase",
        "Loopback",
        "MSBuild",
        "PluginLoad",
        "PowerShell",
        "ProcessEnvironment",
        "Reflection",
        "Release",
        "Roslyn",
        "SharedOutputDirectory"
    ];

    private static readonly HashSet<string> AllowedTestConcerns =
    [
        "Security",
        "Reliability",
        "Compatibility",
        "Accessibility",
        "Performance"
    ];

    private static readonly HashSet<string> AllowedTestRisks = ["P0", "P1", "P2"];

    private static readonly HashSet<string> AllowedRunnerModes = ["Parallel", "Serial"];

    private static readonly HashSet<string> AllowedTestProfiles =
    [
        "Default",
        "Simulation",
        "GoldenDataset",
        "LiveExternal"
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedRuntimesByKind =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Aggregate"] = ["Pure"],
            ["Application"] = ["Pure"],
            ["Architecture"] = ["Pure", "Filesystem"],
            ["Conformance"] = ["Pure", "Filesystem"],
            ["Contract"] = ["Pure", "Filesystem", "Network"],
            ["Deployment"] = ["Filesystem", "Windows"],
            ["Integration"] = ["Pure", "Filesystem", "Network", "SQLite"],
            ["Persistence"] = ["Filesystem", "SQLite"],
            ["UI"] = ["Avalonia"],
            ["Unit"] = ["Pure"],
            ["Workflow"] = ["Pure", "Filesystem", "SQLite"]
        };

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
            var relativePath = Normalize(Path.GetRelativePath(root, path));
            foreach (var property in RequiredTestMetadata)
            {
                var values = GetDirectProjectProperties(project, property);
                Assert.True(
                    values.Length == 1,
                    $"{relativePath} must declare direct {property} exactly once; actual={values.Length}.");
                if (property != "TestRuntimeDependencies")
                {
                    Assert.False(
                        string.IsNullOrWhiteSpace(values[0]),
                        $"{relativePath} direct {property} cannot be empty.");
                }
            }

            var testKind = GetProjectProperty(project, "TestKind")!;
            var runtime = GetProjectProperty(project, "TestRuntime")!;
            var runtimeDependencies = (GetProjectProperty(project, "TestRuntimeDependencies") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var runnerMode = GetProjectProperty(project, "TestRunnerMode")!;
            var risk = GetProjectProperty(project, "TestRisk")!;
            var concern = GetProjectProperty(project, "TestConcern")!;
            var profile = GetProjectProperty(project, "TestProfile")!;
            Assert.Contains(testKind, AllowedTestKinds);
            Assert.Contains(runtime, AllowedTestRuntimes);
            Assert.Equal(
                runtimeDependencies.Length,
                runtimeDependencies.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(
                runtimeDependencies,
                dependency => dependency == "None" || !AllowedRuntimeDependencies.Contains(dependency));
            Assert.Contains(risk, AllowedTestRisks);
            Assert.Contains(concern, AllowedTestConcerns);
            Assert.Contains(profile, AllowedTestProfiles);
            Assert.Contains(runnerMode, AllowedRunnerModes);
            Assert.True(
                runtime == "Pure" ? runnerMode == "Parallel" : runnerMode == "Serial",
                $"{relativePath} must use Parallel only for Pure tests and Serial for resource-backed tests.");
            Assert.True(
                !AllowedRuntimesByKind.TryGetValue(testKind, out var allowedRuntimes) ||
                allowedRuntimes.Contains(runtime),
                $"{relativePath} TestKind={testKind} is incompatible with TestRuntime={runtime}.");

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
    public void PluginTestFixtures_ShouldRemainNonProductionAndNonExecutable()
    {
        var root = FindRepositoryRoot();
        var fixtures = EnumerateSourceFiles(Path.Combine(root, "src"), "*.csproj")
            .Select(path => (Path: path, Project: XDocument.Load(path)))
            .Where(item => IsTrue(GetProjectProperty(item.Project, "IsEdgePluginTestFixture")))
            .ToArray();
        Assert.NotEmpty(fixtures);

        foreach (var fixture in fixtures)
        {
            var relativePath = Normalize(Path.GetRelativePath(root, fixture.Path));
            Assert.True(
                relativePath.StartsWith("src/Testing/", StringComparison.Ordinal),
                $"{relativePath} must remain below src/Testing.");
            Assert.True(IsTrue(GetProjectProperty(fixture.Project, "IsEdgePluginModule")));
            Assert.True(
                string.Equals(
                    "false",
                    GetProjectProperty(fixture.Project, "IsPackable"),
                    StringComparison.OrdinalIgnoreCase),
                $"{relativePath} must declare IsPackable=false.");
            Assert.False(IsTrue(GetProjectProperty(fixture.Project, "IsTestProject")));
        }
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
    public void PlcRuntimeApplyService_ProductionInvocation_ShouldRemainOwnedByGuardedHardwareTransaction()
    {
        var root = FindRepositoryRoot();
        var callers = EnumerateSourceFiles(Path.Combine(root, "src"), "*.cs")
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Testing{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path)
                .Contains(".ApplyDeviceRuntimeAsync(", StringComparison.Ordinal))
            .Select(path => Normalize(Path.GetRelativePath(root, path)))
            .ToArray();

        Assert.Equal(
            [
                "src/Application/IIoT.Edge.Application/Features/Hardware/HardwareConfig/HardwareConfigQueries.cs"
            ],
            callers);
    }

    [GeneratedRegex("<Project\\s+Path=\"([^\"]+\\.csproj)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectPathPattern();

    [GeneratedRegex(@"\[(?:Xunit\.)?(?:Fact|Theory)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TestAttributePattern();

    private static string? GetProjectProperty(XDocument project, string propertyName) =>
        GetDirectProjectProperties(project, propertyName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string[] GetDirectProjectProperties(XDocument project, string propertyName) =>
        project.Root?
            .Elements("PropertyGroup")
            .Elements(propertyName)
            .Select(element => element.Value.Trim())
            .ToArray()
        ?? [];

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

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
