using IIoT.Edge.Shell.Core;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.SharedKernel.Configuration;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace IIoT.Edge.Shell.FilesystemTests;

public sealed class SingleInstanceMutexHandleBehaviorTests
{
    [Fact]
    public void TryAcquire_WhenExistingMutexIsNotOwnedByCaller_ShouldDisposeHandleImmediately()
    {
        var mutexName = $"IIoT.Edge.Shell.FilesystemTests.{Guid.NewGuid():N}";

        using var first = new SingleInstanceMutexHandle();
        using var second = new SingleInstanceMutexHandle();
        using var third = new SingleInstanceMutexHandle();

        Assert.True(first.TryAcquire(mutexName));
        Assert.False(second.TryAcquire(mutexName));

        second.Release();
        first.Release();

        Assert.True(third.TryAcquire(mutexName));
    }

    [Fact]
    public void Release_WhenCalledMultipleTimes_ShouldRemainSafe()
    {
        var mutexName = $"IIoT.Edge.Shell.FilesystemTests.{Guid.NewGuid():N}";

        using var handle = new SingleInstanceMutexHandle();

        Assert.True(handle.TryAcquire(mutexName));

        handle.Release();
        handle.Release();
    }

    [Fact]
    public void TryAcquireNonBlocking_WhenMutexNameIsInvalid_ShouldReturnUnavailableWithoutThrowing()
    {
        using var handle = new SingleInstanceMutexHandle();

        var result = handle.TryAcquireNonBlocking("invalid\0mutex", out var failure);

        Assert.Equal(SingleInstanceMutexAcquireResult.Unavailable, result);
        Assert.NotNull(failure);
        Assert.False(handle.OwnsMutex);
    }

    [Theory]
    [InlineData("../../Line-A")]
    [InlineData("Line-A\\Other")]
    [InlineData("Line-A\nInjected")]
    public void InstanceMutexName_WhenInstanceIdContainsUnsafeCharacters_ShouldBeSafeAndStable(string instanceId)
    {
        var first = EdgeClientInstanceMutexName.Create(instanceId);
        var second = EdgeClientInstanceMutexName.Create(instanceId);

        Assert.Equal(first, second);
        Assert.StartsWith("Global\\IIoT.EdgeClient_", first, StringComparison.Ordinal);
        var instanceSegment = first["Global\\IIoT.EdgeClient_".Length..];
        Assert.DoesNotContain("..", instanceSegment, StringComparison.Ordinal);
        Assert.DoesNotContain('/', instanceSegment);
        Assert.DoesNotContain('\\', instanceSegment);
        Assert.DoesNotContain('\n', instanceSegment);
        Assert.True(instanceSegment.Length <= 96);
    }
}

public sealed class CrashLogWriterBehaviorTests
{
    [Fact]
    public void Write_WhenPrimaryPathFails_ShouldFallbackToSecondaryPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var diagnosticMessages = new List<string>();
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                (path, entry) =>
            {
                if (string.Equals(path, primaryPath, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("primary blocked");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, entry, Encoding.UTF8);
            },
                diagnosticMessages.Add);

            writer.Write("fatal-source", new InvalidOperationException("boom"), "details");

            Assert.Empty(diagnosticMessages);
            var content = File.ReadAllText(fallbackPath);
            Assert.Contains("fatal-source", content, StringComparison.Ordinal);
            Assert.Contains("primary_result=failed", content, StringComparison.Ordinal);
            Assert.Contains("fallback_result=succeeded", content, StringComparison.Ordinal);
            Assert.Contains("primary blocked", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Write_WhenPrimaryAndFallbackPathsFail_ShouldEmitDiagnosticSignal()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var diagnosticMessages = new List<string>();
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                (path, _) =>
            {
                if (string.Equals(path, primaryPath, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("primary blocked");
                }

                throw new IOException("fallback blocked");
            },
                diagnosticMessages.Add);

            writer.Write("fatal-source", new InvalidOperationException("boom"), "details");

            var message = Assert.Single(diagnosticMessages);
            Assert.Contains("primary_result=failed", message, StringComparison.Ordinal);
            Assert.Contains("fallback_result=failed", message, StringComparison.Ordinal);
            Assert.Contains("primary blocked", message, StringComparison.Ordinal);
            Assert.Contains("fallback blocked", message, StringComparison.Ordinal);
            Assert.Contains("fatal-source", message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Write_WhenAllCrashLogSinksFail_ShouldSurfaceDiagnosticFailure()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var primaryPath = Path.Combine(tempDir, "primary", "crash.log");
            var fallbackPath = Path.Combine(tempDir, "fallback", "crash.log");
            var writer = new CrashLogWriter(
                () => primaryPath,
                () => fallbackPath,
                static (_, _) => throw new IOException("file sinks blocked"),
                static _ => throw new InvalidOperationException("diagnostic sink blocked"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                writer.Write("fatal-source", new InvalidOperationException("boom"), "details"));

            Assert.Equal("diagnostic sink blocked", error.Message);
        }
        finally
        {
            DeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-shell-core-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public sealed class StartupExceptionAllowlistSourceGuardTests
{
    private static readonly string[] GuardedSourcePaths =
    [
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/ShellConfigurationLoader.cs",
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/ShellRuntimePathResolver.cs",
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/DirectoryModuleCatalog.cs",
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/ModulePluginLoader.cs",
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/ModulePluginAssemblyResolver.cs",
        "src/Edge/IIoT.Edge.Shell/Modules/ShellModuleCatalog.cs",
        "src/Edge/IIoT.Edge.Host.Bootstrap/Core/EdgeRuntimePathPreflight.cs"
    ];

    [Fact]
    public void StartupAdapterSources_ShouldNotContainUnfilteredCatchAll()
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var relativePath in GuardedSourcePaths)
        {
            var source = File.ReadAllText(Path.Combine(repositoryRoot, relativePath));

            Assert.False(
                Regex.IsMatch(source, @"catch\s*\{"),
                $"Bare catch is forbidden in startup adapter source: {relativePath}");
            Assert.False(
                Regex.IsMatch(source, @"catch\s*\(\s*Exception\s+\w+\s*\)\s*\{"),
                $"Unfiltered Exception catch is forbidden in startup adapter source: {relativePath}");
        }
    }

    [Fact]
    public void PluginCatalogSources_ShouldKeepTypedFailureBoundaryAndNoOuterDiscoveryFallback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configurationLoaderSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/Edge/IIoT.Edge.Host.Bootstrap/Core/ShellConfigurationLoader.cs"));
        var directoryCatalogSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/Edge/IIoT.Edge.Host.Bootstrap/Core/Modules/DirectoryModuleCatalog.cs"));

        Assert.DoesNotContain("PLUGIN_DEFAULT_DISCOVERY_FAILED", configurationLoaderSource, StringComparison.Ordinal);
        Assert.Contains("catch (ModulePluginManifestException ex)", directoryCatalogSource, StringComparison.Ordinal);
        Assert.Contains("catch (ModulePluginLoadException ex)", directoryCatalogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellLaunchReadySignal_ShouldFollowSuccessfulLifecycleAndShownMainWindow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src/Edge/IIoT.Edge.Shell/App.axaml.cs"));
        const string acquirePresence =
            "EdgeClientUpdateCoordination.TryAcquireShellPresence(";
        const string loadConfiguration =
            "_configurationLoader.Load(";
        const string lifecycleFailureGuard = "if (!startupResult.Success)";
        const string showMainWindow = "mainWindow.Show();";
        const string validateTargetModules =
            "ShellModuleLaunchReadiness.Evaluate(";
        const string signalReady =
            "EdgeClientUpdateCoordination.TrySignalShellLaunchReady(";
        const string signalReadyWithDiagnostics =
            "EdgeClientUpdateCoordination.TrySignalShellLaunchReadyWithDiagnostics(";

        var acquirePresenceIndex = source.IndexOf(
            acquirePresence,
            StringComparison.Ordinal);
        var loadConfigurationIndex = source.IndexOf(
            loadConfiguration,
            StringComparison.Ordinal);
        var failureGuardIndex = source.IndexOf(
            lifecycleFailureGuard,
            StringComparison.Ordinal);
        var showIndex = source.IndexOf(
            showMainWindow,
            StringComparison.Ordinal);
        var moduleValidationIndex = source.IndexOf(
            validateTargetModules,
            StringComparison.Ordinal);
        var signalIndex = source.IndexOf(
            signalReady,
            StringComparison.Ordinal);
        var diagnosticSignalIndex = source.IndexOf(
            signalReadyWithDiagnostics,
            StringComparison.Ordinal);

        Assert.True(acquirePresenceIndex >= 0);
        Assert.True(loadConfigurationIndex > acquirePresenceIndex);
        Assert.True(failureGuardIndex > loadConfigurationIndex);
        Assert.True(showIndex > failureGuardIndex);
        Assert.True(moduleValidationIndex > showIndex);
        Assert.True(signalIndex > moduleValidationIndex);
        Assert.True(diagnosticSignalIndex > moduleValidationIndex);
        Assert.Equal(
            signalIndex,
            source.LastIndexOf(signalReady, StringComparison.Ordinal));
    }

    [Fact]
    public void ShellLaunchDiagnostics_ShouldExposeOnlyStableCodeModuleAndRepairTarget()
    {
        const string sensitiveMessage = "connection string and local path";

        var diagnostics = IIoT.Edge.Shell.App.BuildShellLaunchDiagnostics(
        [
            new StartupDiagnosticIssue(
                "STARTUP_CLOUD_RETRY_TASK_FAILED",
                sensitiveMessage,
                "AP")
        ]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("STARTUP_CLOUD_RETRY_TASK_FAILED", diagnostic.ReasonCode);
        Assert.Equal("AP", diagnostic.ModuleId);
        Assert.Equal("System.Diagnostics", diagnostic.RepairTarget);
        Assert.DoesNotContain(sensitiveMessage, diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShellLaunchDiagnostics_ShouldIncludeCurrentBackgroundWorkerFaults()
    {
        var diagnostics = IIoT.Edge.Shell.App.BuildShellLaunchDiagnostics(
            [],
            [
                new IIoT.Edge.Application.Common.Tasks.BackgroundServiceRuntimeSnapshot(
                    "ProcessQueueTask",
                    IIoT.Edge.Application.Common.Tasks.BackgroundServiceRuntimeState.Faulted,
                    DateTime.UtcNow,
                    "BACKGROUND_TASK_EXECUTION_FAULT"),
                new IIoT.Edge.Application.Common.Tasks.BackgroundServiceRuntimeSnapshot(
                    "CloudRetryTask",
                    IIoT.Edge.Application.Common.Tasks.BackgroundServiceRuntimeState.Running,
                    DateTime.UtcNow,
                    null)
            ]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BACKGROUND_TASK_EXECUTION_FAULT", diagnostic.ReasonCode);
        Assert.Equal("ProcessQueueTask", diagnostic.ModuleId);
        Assert.Equal("System.Diagnostics", diagnostic.RepairTarget);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IIoT.EdgeClient repository root.");
    }
}
