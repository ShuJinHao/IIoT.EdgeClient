using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;
using System.Security;
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
    public void ResolveWithDiagnostics_WhenRuntimeRootIsInvalid_ShouldFailClosed()
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
            Assert.False(result.Success);
            Assert.Contains(result.Issues, issue => issue.Code == "RUNTIME_PROFILE_NAME_SANITIZED");
            Assert.Contains(result.Issues, issue => issue.Code == "RUNTIME_DATA_ROOT_INVALID");
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(SecurityException))]
    public void ResolveWithDiagnostics_WhenDefaultPathCannotBeResolved_ShouldFailClosed(Type exceptionType)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        try
        {
            var expected = (Exception)Activator.CreateInstance(exceptionType, "recoverable path failure")!;
            var resolver = new ShellRuntimePathResolver(
                (_, _) => throw expected,
                (_, _) => Path.Combine(baseDirectory, "configured"),
                (_, _) => Path.Combine(baseDirectory, "fallback-crash.log"));

            var result = resolver.ResolveWithDiagnostics(
                baseDirectory,
                new ConfigurationBuilder().Build());

            Assert.Equal(
                Path.Combine(baseDirectory, "runtime-data", "Default"),
                result.RuntimePaths.RuntimeDataRoot);
            Assert.False(result.Success);
            Assert.Contains(result.Issues, issue => issue.Code == "RUNTIME_DEFAULT_ROOT_INVALID");
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("configured", typeof(ArgumentException))]
    [InlineData("configured", typeof(NotSupportedException))]
    [InlineData("configured", typeof(IOException))]
    [InlineData("configured", typeof(UnauthorizedAccessException))]
    [InlineData("configured", typeof(SecurityException))]
    [InlineData("fallback", typeof(ArgumentException))]
    [InlineData("fallback", typeof(NotSupportedException))]
    [InlineData("fallback", typeof(IOException))]
    [InlineData("fallback", typeof(UnauthorizedAccessException))]
    [InlineData("fallback", typeof(SecurityException))]
    public void ResolveWithDiagnostics_WhenApprovedConfiguredOrFallbackPathExceptionOccurs_ShouldReportStableDiagnostic(
        string boundary,
        Type exceptionType)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        try
        {
            var expected = (Exception)Activator.CreateInstance(exceptionType, "recoverable path failure")!;
            var callCount = 0;
            string ResolveOrThrow(string currentBoundary, string fallback)
            {
                if (!string.Equals(boundary, currentBoundary, StringComparison.Ordinal))
                    return fallback;

                callCount++;
                throw expected;
            }

            var profileDefault = Path.Combine(baseDirectory, "profile-default");
            var resolver = new ShellRuntimePathResolver(
                (_, _) => profileDefault,
                (_, _) => ResolveOrThrow("configured", Path.Combine(baseDirectory, "configured")),
                (_, _) => ResolveOrThrow("fallback", Path.Combine(baseDirectory, "fallback-crash.log")));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:RuntimeDataRoot"] = "configured-root"
                })
                .Build();

            var result = resolver.ResolveWithDiagnostics(baseDirectory, configuration);

            Assert.Equal(1, callCount);
            Assert.Equal(boundary == "fallback", result.Success);
            Assert.Contains(
                result.Issues,
                issue => issue.Code == (boundary == "configured"
                    ? "RUNTIME_DATA_ROOT_INVALID"
                    : "RUNTIME_FALLBACK_CRASH_PATH_INVALID"));
            if (boundary == "configured")
                Assert.Equal(profileDefault, result.RuntimePaths.RuntimeDataRoot);
            else
                Assert.Equal(
                    Path.Combine(result.RuntimePaths.DiagnosticsDirectory, "crash.fallback.log"),
                    result.RuntimePaths.FallbackCrashLogPath);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("configured")]
    [InlineData("fallback")]
    public void ResolveWithDiagnostics_WhenPathBoundaryThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce(
        string boundary)
    {
        AssertPathBoundaryExceptionPropagates(
            boundary,
            new InvalidOperationException($"unexpected {boundary} failure"));
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("configured")]
    [InlineData("fallback")]
    public void ResolveWithDiagnostics_WhenPathBoundaryThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce(
        string boundary)
    {
        AssertPathBoundaryExceptionPropagates(
            boundary,
            new OperationCanceledException($"{boundary} canceled"));
    }

    private static void AssertPathBoundaryExceptionPropagates(string boundary, Exception expected)
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        try
        {
            var callCount = 0;
            string ResolveOrThrow(string currentBoundary, string fallback)
            {
                if (!string.Equals(boundary, currentBoundary, StringComparison.Ordinal))
                    return fallback;

                callCount++;
                throw expected;
            }

            var resolver = new ShellRuntimePathResolver(
                (_, _) => ResolveOrThrow("profile", Path.Combine(baseDirectory, "profile-default")),
                (_, _) => ResolveOrThrow("configured", Path.Combine(baseDirectory, "configured")),
                (_, _) => ResolveOrThrow("fallback", Path.Combine(baseDirectory, "fallback-crash.log")));
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:RuntimeDataRoot"] = "configured-root"
                })
                .Build();

            var actual = Assert.Throws(expected.GetType(), () =>
                resolver.ResolveWithDiagnostics(baseDirectory, configuration));

            Assert.Same(expected, actual);
            Assert.Equal(1, callCount);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Preflight_WhenRuntimeDataRootCannotBeCreated_ShouldFailClosedWithoutChangingRoot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var blockedRuntimeRoot = Path.Combine(tempDirectory, "blocked-runtime-root");
        var profileName = "PreflightTest-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(blockedRuntimeRoot, "blocks directory creation");
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, blockedRuntimeRoot);

            var result = EdgeRuntimePathPreflight.EnsureWritable(runtimePaths);

            Assert.False(result.Success);
            Assert.Equal(blockedRuntimeRoot, result.RuntimePaths.RuntimeDataRoot);
            Assert.False(Directory.Exists(result.RuntimePaths.DatabaseDirectory));
            var issue = Assert.Single(result.Issues);
            Assert.Equal("RUNTIME_DATA_ROOT_UNAVAILABLE", issue.Code);
            Assert.Contains(blockedRuntimeRoot, issue.Message, StringComparison.Ordinal);
            Assert.Contains("不会改用备用目录", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void Preflight_WhenPrimaryDirectoryWriteReplaceProbeFails_ShouldFailClosedWithoutChangingRoot()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var primaryRoot = Path.Combine(tempDirectory, "primary");
        var profileName = "WriteProbe-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, primaryRoot);
            var result = EdgeRuntimePathPreflight.EnsureWritable(
                runtimePaths,
                directory => string.Equals(directory, primaryRoot, StringComparison.Ordinal)
                    ? new IOException("write/replace denied")
                    : null);

            Assert.False(result.Success);
            Assert.Equal(primaryRoot, result.RuntimePaths.RuntimeDataRoot);
            var issue = Assert.Single(result.Issues);
            Assert.Equal("RUNTIME_DATA_ROOT_UNAVAILABLE", issue.Code);
            Assert.Contains("write/replace denied", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Preflight_WhenProbeReturnsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertPreflightProbeExceptionPropagates(new InvalidOperationException("unexpected probe failure"));
    }

    [Fact]
    public void Preflight_WhenProbeReturnsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertPreflightProbeExceptionPropagates(new OperationCanceledException("probe canceled"));
    }

    private static void AssertPreflightProbeExceptionPropagates(Exception expected)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var callCount = 0;
            var runtimePaths = CreateRuntimePaths(tempDirectory, "UnknownProbe", Path.Combine(tempDirectory, "runtime"));

            var actual = Assert.Throws(expected.GetType(), () =>
                EdgeRuntimePathPreflight.EnsureWritable(
                    runtimePaths,
                    _ =>
                    {
                        callCount++;
                        return expected;
                    }));

            Assert.Same(expected, actual);
            Assert.Equal(1, callCount);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Preflight_WhenFirstDirectoryProbeFails_ShouldStopWithoutProbingFallback()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var profileName = "SingleProbe-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, Path.Combine(tempDirectory, "runtime"));

            var result = EdgeRuntimePathPreflight.EnsureWritable(
                runtimePaths,
                directory =>
                {
                    counts[directory] = counts.GetValueOrDefault(directory) + 1;
                    return new IOException("probe denied");
                });

            Assert.False(result.Success);
            Assert.Equal("RUNTIME_DATA_ROOT_UNAVAILABLE", Assert.Single(result.Issues).Code);
            Assert.Single(counts);
            Assert.Equal(1, counts.Values.Single());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(SecurityException))]
    public void Preflight_WhenApprovedPrimaryProbeFailureOccurs_ShouldFailClosedExactlyOnce(Type exceptionType)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        var profileName = "ApprovedProbe-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var expected = (Exception)Activator.CreateInstance(exceptionType, "recoverable probe failure")!;
            var primaryRoot = Path.Combine(tempDirectory, "runtime");
            var primaryCallCount = 0;
            var runtimePaths = CreateRuntimePaths(tempDirectory, profileName, primaryRoot);

            var result = EdgeRuntimePathPreflight.EnsureWritable(
                runtimePaths,
                directory =>
                {
                    if (!string.Equals(directory, primaryRoot, StringComparison.OrdinalIgnoreCase))
                        return null;

                    primaryCallCount++;
                    return expected;
                });

            Assert.Equal(1, primaryCallCount);
            Assert.False(result.Success);
            Assert.Equal("RUNTIME_DATA_ROOT_UNAVAILABLE", Assert.Single(result.Issues).Code);
            Assert.Equal(primaryRoot, result.RuntimePaths.RuntimeDataRoot);
        }
        finally
        {
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

            Assert.True(result.Success);
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
