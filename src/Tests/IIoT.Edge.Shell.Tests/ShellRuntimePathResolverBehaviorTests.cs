using IIoT.Edge.SharedKernel.Configuration;
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
            var programDataRoot = Path.Combine(baseDirectory, "program-data");
            var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Shell:MachineProfile"] = "HomogenizationLine"
                    })
                    .Build();

            EdgeEnvironmentTestScope.WithProgramDataRoot(programDataRoot, () =>
            {
                var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

                Assert.Equal("HomogenizationLine", result.ProfileName);
                Assert.Equal(
                    Path.Combine(programDataRoot, "IIoT", "EdgeData", "profiles", "HomogenizationLine"),
                    result.RuntimeDataRoot);
                Assert.Equal(Path.Combine(result.RuntimeDataRoot, "db"), result.DatabaseDirectory);
                Assert.Equal(Path.Combine(result.RuntimeDataRoot, "context"), result.ContextDirectory);
                Assert.Equal(Path.Combine(result.DiagnosticsDirectory, "logs"), result.LogDirectory);
            });
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenRuntimeDataRootUsesWindowsSeparators_ShouldResolveRelativeRootAgainstBaseDirectory()
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

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

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
        var programDataRoot = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"), "program-data");
        var expectedRuntimeRoot = Path.Combine(programDataRoot, "IIoT", "EdgeData", "profiles", "HomogenizationLine");

        var profileText = File.ReadAllText(machineProfilePath);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(machineProfilePath, optional: false)
            .Build();

        EdgeEnvironmentTestScope.WithProgramDataRoot(programDataRoot, () =>
        {
            var result = new ShellRuntimePathResolver().Resolve(shellOutputDirectory, configuration);

            Assert.DoesNotContain("%LocalAppData%", profileText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%ProgramData%/IIoT/EdgeData/profiles/HomogenizationLine", profileText, StringComparison.Ordinal);
            Assert.DoesNotContain("../data/profiles", profileText, StringComparison.Ordinal);
            Assert.DoesNotContain(@"..\\data\\profiles", profileText, StringComparison.Ordinal);
            Assert.Equal(expectedRuntimeRoot, result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(expectedRuntimeRoot, "db"), result.DatabaseDirectory);
        });
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

internal static class EdgeEnvironmentTestScope
{
    public static void WithProgramDataRoot(string programDataRoot, Action action)
    {
        var originalValue = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, programDataRoot);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, originalValue);
        }
    }
}
