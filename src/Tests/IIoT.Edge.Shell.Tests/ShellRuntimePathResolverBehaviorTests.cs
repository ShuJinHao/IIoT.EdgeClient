using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ShellRuntimePathResolverBehaviorTests
{
    [Fact]
    public void Resolve_WhenRuntimeDataRootIsMissing_ShouldUseProfileScopedDefaultDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "HomogenizationLine"
                })
                .Build();

            var result = ShellRuntimePathResolver.Resolve(baseDirectory, configuration);

            Assert.Equal("HomogenizationLine", result.ProfileName);
            Assert.Equal(Path.Combine(baseDirectory, "data", "profiles", "HomogenizationLine"), result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "db"), result.DatabaseDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "context"), result.ContextDirectory);
            Assert.Equal(Path.Combine(result.DiagnosticsDirectory, "logs"), result.LogDirectory);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenRuntimeDataRootIsConfigured_ShouldResolveRelativeRootAgainstBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "HomogenizationLine",
                    ["Shell:RuntimeDataRoot"] = ".\\profiles\\homogenization"
                })
                .Build();

            var result = ShellRuntimePathResolver.Resolve(baseDirectory, configuration);

            Assert.Equal(
                Path.Combine(baseDirectory, "profiles", "homogenization"),
                result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "recipe"), result.RecipeDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "excel"), result.ExcelDirectory);
            Assert.Contains("HomogenizationLine", result.FallbackCrashLogPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void HomogenizationMachineProfile_ShouldStoreRuntimeDataUnderPublishRoot()
    {
        var repoRoot = FindRepoRoot();
        var machineProfilePath = Path.Combine(
            repoRoot,
            "src",
            "Edge",
            "IIoT.Edge.Shell",
            "appsettings.machine.HomogenizationLine.json");
        var shellOutputDirectory = Path.Combine(repoRoot, "publish", "Debug", "homogenization");
        var expectedRuntimeRoot = Path.GetFullPath(Path.Combine(repoRoot, "publish", "Debug", "data", "profiles", "HomogenizationLine"));

        var profileText = File.ReadAllText(machineProfilePath);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(machineProfilePath, optional: false)
            .Build();

        var result = ShellRuntimePathResolver.Resolve(shellOutputDirectory, configuration);

        Assert.DoesNotContain("%LocalAppData%", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedRuntimeRoot, result.RuntimeDataRoot);
        Assert.Equal(Path.Combine(expectedRuntimeRoot, "db"), result.DatabaseDirectory);
    }

    private static string FindRepoRoot()
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

        throw new InvalidOperationException("Could not locate IIoT.EdgeClient repository root.");
    }
}
