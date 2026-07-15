using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIoT.Edge.Shell.FilesystemTests;

public sealed class ShellRuntimePathResolverBehaviorTests
{
    [Fact]
    public void Resolve_WhenRuntimeDataRootIsMissing_ShouldUseProfileScopedDefaultDirectory()
    {
        var layoutRoot = Path.Combine(
            Path.GetTempPath(),
            "edge-runtime-resolver-tests",
            Guid.NewGuid().ToString("N"));
        var baseDirectory = Path.Combine(layoutRoot, "test-plugin");
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Shell:MachineProfile"] = "TestMachineProfile"
                    })
                    .Build();

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

            Assert.Equal("TestMachineProfile", result.ProfileName);
            Assert.Equal(
                Path.Combine(layoutRoot, "data", "IIoT", "EdgeData", "profiles", "TestMachineProfile"),
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
        var baseDirectory = Path.Combine(appRoot, "current", "test-plugin");
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "TestMachineProfile"
                })
                .Build();

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

            Assert.Equal(
                Path.Combine(appRoot, "data", "IIoT", "EdgeData", "profiles", "TestMachineProfile"),
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
                    ["Shell:MachineProfile"] = "TestMachineProfile",
                    ["Shell:RuntimeDataRoot"] = ".\\profiles\\test-plugin"
                })
                .Build();

            var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

            Assert.Equal(
                Path.Combine(baseDirectory, "profiles", "test-plugin"),
                result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "recipe"), result.RecipeDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "excel"), result.ExcelDirectory);
            Assert.Contains("TestMachineProfile", result.FallbackCrashLogPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
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
                    ["Shell:MachineProfile"] = "TestMachineProfile"
                })
                .Build();

            EdgeEnvironmentTestScope.WithDataRootOverride(overrideRoot, () =>
            {
                var result = new ShellRuntimePathResolver().Resolve(baseDirectory, configuration);

                Assert.Equal(
                    Path.Combine(overrideRoot, "IIoT", "EdgeData", "profiles", "TestMachineProfile"),
                    result.RuntimeDataRoot);
            });
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveWithDiagnostics_WhenRuntimeRootIsInvalid_ShouldUseProfileDefaultWithoutThrowing()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "../../UnsafeProfile",
                    ["Shell:RuntimeDataRoot"] = "bad\0root"
                })
                .Build();

            var result = new ShellRuntimePathResolver().ResolveWithDiagnostics(baseDirectory, configuration);

            Assert.DoesNotContain("..", result.RuntimePaths.ProfileName, StringComparison.Ordinal);
            Assert.DoesNotContain("UnsafeProfile" + Path.DirectorySeparatorChar + "..", result.RuntimePaths.RuntimeDataRoot, StringComparison.Ordinal);
            Assert.Contains(result.Issues, issue => issue.Code == "RUNTIME_PROFILE_NAME_SANITIZED");
            Assert.Contains(result.Issues, issue => issue.Code == "RUNTIME_DATA_ROOT_INVALID");
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

    [Fact]
    public void Preflight_WhenPrimaryDirectoryWriteReplaceProbeFails_ShouldUseFallbackAndRemoveProbeArtifacts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var primaryRoot = Path.Combine(tempDirectory, "primary");
        var profileName = "WriteProbe-" + Guid.NewGuid().ToString("N");
        EdgeRuntimePathPreflightResult? result = null;
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, primaryRoot);
            result = EdgeRuntimePathPreflight.EnsureWritable(
                runtimePaths,
                directory => string.Equals(directory, primaryRoot, StringComparison.Ordinal)
                    ? new IOException("write/replace denied")
                    : null);

            Assert.NotEqual(primaryRoot, result.RuntimePaths.RuntimeDataRoot);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("RUNTIME_DATA_ROOT_FALLBACK", issue.Code);
            Assert.Contains("write/replace denied", issue.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(
                result.RuntimePaths.RuntimeDataRoot,
                ".iiot-edge-write-probe-*",
                SearchOption.AllDirectories));
        }
        finally
        {
            if (result?.RuntimePaths.RuntimeDataRoot is { } fallbackRoot
                && Directory.Exists(fallbackRoot))
            {
                Directory.Delete(fallbackRoot, recursive: true);
            }

            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Preflight_WhenPrimaryRuntimeTreeIsWritable_ShouldVerifyWriteReplaceAndLeaveNoArtifacts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var runtimePaths = CreateRuntimePaths(tempDirectory, "WritableProbe", Path.Combine(tempDirectory, "runtime"));

            var result = EdgeRuntimePathPreflight.EnsureWritable(runtimePaths);

            Assert.Empty(result.Issues);
            Assert.Equal(runtimePaths.RuntimeDataRoot, result.RuntimePaths.RuntimeDataRoot);
            Assert.Empty(Directory.GetFiles(
                runtimePaths.RuntimeDataRoot,
                ".iiot-edge-write-probe-*",
                SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
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
