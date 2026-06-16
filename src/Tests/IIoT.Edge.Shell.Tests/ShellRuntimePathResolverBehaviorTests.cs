using IIoT.Edge.Application.Abstractions.Config;
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
        var layoutRoot = Path.Combine(
            Path.GetTempPath(),
            "edge-runtime-resolver-tests",
            Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(layoutRoot, "homogenization");
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Shell:MachineProfile"] = "HomogenizationLine"
                    })
                    .Build();

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

            Assert.Equal("HomogenizationLine", result.ProfileName);
            Assert.Equal(
                Path.Combine(layoutRoot, "data", "IIoT", "EdgeData", "profiles", "HomogenizationLine"),
                result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "db"), result.DatabaseDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "context"), result.ContextDirectory);
            Assert.Equal(Path.Combine(result.DiagnosticsDirectory, "logs"), result.LogDirectory);
        }
        finally
        {
            Directory.Delete(layoutRoot, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenRunningUnderVelopackCurrent_ShouldStoreRuntimeDataBesideCurrent()
    {
        var appRoot = Path.Combine(
            Path.GetTempPath(),
            "edge-runtime-resolver-tests",
            Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(appRoot, "current", "homogenization");
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "HomogenizationLine"
                })
                .Build();

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

            Assert.Equal(
                Path.Combine(appRoot, "data", "IIoT", "EdgeData", "profiles", "HomogenizationLine"),
                result.RuntimeDataRoot);
        }
        finally
        {
            Directory.Delete(appRoot, recursive: true);
        }
    }

    [Fact]
    public void ProgramDataPaths_WhenRunningFromVelopackCurrentRoot_ShouldKeepMutableDataOutsideCurrent()
    {
        var appRoot = Path.Combine(
            Path.GetTempPath(),
            "edge-runtime-resolver-tests",
            Guid.NewGuid().ToString("N"));
        var previousDataRoot = Environment.GetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        var currentRoot = Path.Combine(appRoot, "current");
        var hostDirectory = Path.Combine(currentRoot, "host");
        Directory.CreateDirectory(hostDirectory);

        try
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                null);

            Assert.Equal(
                Path.Combine(appRoot, "data"),
                EdgeClientProgramDataPaths.ResolveApplicationDataRoot(currentRoot));
            Assert.Equal(
                Path.Combine(appRoot, "data"),
                EdgeClientProgramDataPaths.ResolveApplicationDataRoot(hostDirectory));
            Assert.Equal(
                Path.Combine(appRoot, "plugins"),
                EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(hostDirectory));
            Assert.Equal(
                Path.Combine(appRoot, "data", "IIoT", "EdgeClient", "launcher"),
                EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentRoot));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                previousDataRoot);
            Directory.Delete(appRoot, recursive: true);
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
        var expectedRuntimeRoot = Path.Combine(
            repoRoot,
            "publish",
            "Debug",
            "data",
            "IIoT",
            "EdgeData",
            "profiles",
            "HomogenizationLine");

        var profileText = File.ReadAllText(machineProfilePath);
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(machineProfilePath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:MachineProfile"] = "HomogenizationLine"
            })
            .Build();

        var result = new ShellRuntimePathResolver().Resolve(shellOutputDirectory, configuration);

        Assert.DoesNotContain("%LocalAppData%", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%ProgramData%", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommonApplicationData", profileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("../data/profiles", profileText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"..\\data\\profiles", profileText, StringComparison.Ordinal);
        Assert.Equal(expectedRuntimeRoot, result.RuntimeDataRoot);
        Assert.Equal(Path.Combine(expectedRuntimeRoot, "db"), result.DatabaseDirectory);
    }

    [Fact]
    public void Resolve_WhenDataRootOverrideIsSet_ShouldHonorExplicitOverride()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var overrideRoot = Path.Combine(baseDirectory, "explicit-data-root");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "HomogenizationLine"
                })
                .Build();

            EdgeEnvironmentTestScope.WithDataRootOverride(overrideRoot, () =>
            {
                var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

                Assert.Equal(
                    Path.Combine(overrideRoot, "IIoT", "EdgeData", "profiles", "HomogenizationLine"),
                    result.RuntimeDataRoot);
            });
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Preflight_WhenRuntimeDataRootCannotBeCreated_ShouldUseFallbackAndReportDiagnosticIssue()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var blockedRuntimeRoot = Path.Combine(tempDirectory, "blocked-runtime-root");
        var profileName = "PreflightTest-" + Guid.NewGuid().ToString("N");
        EdgeRuntimePathPreflightResult? result = null;

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(blockedRuntimeRoot, "blocks directory creation");
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, blockedRuntimeRoot);

            result = EdgeRuntimePathPreflight.EnsureWritable(runtimePaths);

            Assert.NotEqual(blockedRuntimeRoot, result.RuntimePaths.RuntimeDataRoot);
            Assert.True(Directory.Exists(result.RuntimePaths.DatabaseDirectory));
            var issue = Assert.Single(result.Issues);
            Assert.Equal("RUNTIME_DATA_ROOT_FALLBACK", issue.Code);
            Assert.Contains(blockedRuntimeRoot, issue.Message, StringComparison.Ordinal);
            Assert.Contains(result.RuntimePaths.RuntimeDataRoot, issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (result?.RuntimePaths.RuntimeDataRoot is { } fallbackRoot
                && Directory.Exists(fallbackRoot))
            {
                Directory.Delete(fallbackRoot, recursive: true);
            }

            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
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

    private static EdgeRuntimePaths CreateRuntimePaths(
        string baseDirectory,
        string profileName,
        string runtimeDataRoot)
    {
        var diagnosticsDirectory = Path.Combine(runtimeDataRoot, "diagnostics");
        return new EdgeRuntimePaths(
            BaseDirectory: baseDirectory,
            ProfileName: profileName,
            RuntimeDataRoot: runtimeDataRoot,
            DatabaseDirectory: Path.Combine(runtimeDataRoot, "db"),
            ContextDirectory: Path.Combine(runtimeDataRoot, "context"),
            RecipeDirectory: Path.Combine(runtimeDataRoot, "recipe"),
            ExcelDirectory: Path.Combine(runtimeDataRoot, "excel"),
            DiagnosticsDirectory: diagnosticsDirectory,
            LogDirectory: Path.Combine(diagnosticsDirectory, "logs"),
            DeviceCacheFilePath: Path.Combine(runtimeDataRoot, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(diagnosticsDirectory, "crash.log"),
            FallbackCrashLogPath: Path.Combine(diagnosticsDirectory, "crash.fallback.log"));
    }
}

internal static class EdgeEnvironmentTestScope
{
    public static void WithDataRootOverride(string dataRoot, Action action)
    {
        var originalValue = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, originalValue);
        }
    }
}
