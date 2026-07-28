using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class ShellLaunchServiceTests
{
    [Fact]
    public void FileUpdateGate_WhenFirstLauncherHoldsLease_ShouldRejectSecondAndReleaseOnDispose()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-update-gate-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        try
        {
            var firstLauncher = new FileLauncherUpdateOperationGate(baseDirectory);
            var secondLauncher = new FileLauncherUpdateOperationGate(baseDirectory);

            using (var firstLease = firstLauncher.TryAcquire())
            {
                Assert.NotNull(firstLease);
                Assert.Null(secondLauncher.TryAcquire());
            }

            using var secondLease = secondLauncher.TryAcquire();
            Assert.NotNull(secondLease);
        }
        finally
        {
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FileUpdateGate_WhenShellPresenceIsHeld_ShouldAllowOtherShellsButRejectUpdate()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-update-gate-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        try
        {
            using var firstShell =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    baseDirectory);
            using var secondShell =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    baseDirectory);
            Assert.NotNull(firstShell);
            Assert.NotNull(secondShell);
            var gate = new FileLauncherUpdateOperationGate(baseDirectory);

            using var blockedUpdate = gate.TryAcquireUpdate();

            Assert.Null(blockedUpdate);
            secondShell.Dispose();
            firstShell.Dispose();
            using var updateAfterShellExit = gate.TryAcquireUpdate();
            Assert.NotNull(updateAfterShellExit);
        }
        finally
        {
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Launch_WhenUpdateLeaseIsHeld_ShouldRejectBeforeStartingProcess()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-update-gate-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        try
        {
            var updateGate = new FileLauncherUpdateOperationGate(baseDirectory);
            using var updateLease = updateGate.TryAcquire();
            Assert.NotNull(updateLease);
            var starter = new SpyProcessStarter();
            var service = CreateService(
                starter,
                updateOperationGate: new FileLauncherUpdateOperationGate(baseDirectory));
            var profile = new LauncherProfileDefinition(
                "LineA",
                "AP",
                "AP profile",
                null,
                "LineA",
                Path.Combine(baseDirectory, "IIoT.Edge.Shell"),
                "ChartBar",
                "#2563EB");

            var exception = Assert.Throws<InvalidOperationException>(
                () => service.Launch(profile));

            Assert.Contains("更新正在进行", exception.Message, StringComparison.Ordinal);
            Assert.Null(starter.StartInfo);
        }
        finally
        {
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Launch_WhenProfileIsBlockedByFailedRecovery_ShouldRejectBeforeStartingProcess()
    {
        var starter = new SpyProcessStarter();
        var service = CreateService(
            starter,
            updateTransactionRecovery: new BlockedUpdateTransactionRecovery("LineA"));
        var profile = new LauncherProfileDefinition(
            "LineA",
            "AP",
            "AP profile",
            null,
            "LineA",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB");

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.Launch(profile));

        Assert.Contains("事务恢复失败", exception.Message, StringComparison.Ordinal);
        Assert.Null(starter.StartInfo);
    }

    [Fact]
    public void NamedMutexProbe_WhenNamedMutexExistsButIsNotOwned_ShouldReturnFalse()
    {
        var instanceId = $"launcher-probe-unowned-{Guid.NewGuid():N}";
        var mutexName = EdgeClientInstanceMutexName.Create(instanceId);
        using var mutex = new Mutex(initiallyOwned: false, mutexName);
        var probe = new NamedMutexShellInstanceProbe();

        Assert.False(probe.IsInstanceRunning(instanceId));
    }

    [Fact]
    public void NamedMutexProbe_WhenNamedMutexIsOwnedByAnotherThread_ShouldReturnTrue()
    {
        var instanceId = $"launcher-probe-owned-{Guid.NewGuid():N}";
        var mutexName = EdgeClientInstanceMutexName.Create(instanceId);
        using var ownerReady = new ManualResetEventSlim();
        using var releaseOwner = new ManualResetEventSlim();
        var owner = new Thread(() =>
        {
            using var mutex = new Mutex(initiallyOwned: true, mutexName);
            ownerReady.Set();
            releaseOwner.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true
        };
        owner.Start();
        Assert.True(ownerReady.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));

        try
        {
            var probe = new NamedMutexShellInstanceProbe();

            Assert.True(probe.IsInstanceRunning(instanceId));
        }
        finally
        {
            releaseOwner.Set();
            Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void GetExecutableCandidates_WhenWindowsProfileUsesExtensionlessShellName_ShouldPreferExeThenConfiguredThenDll()
    {
        var configuredPath = Path.Combine("runtime", "IIoT.Edge.Shell");

        var candidates = ShellLaunchTargetResolver.GetExecutableCandidates(configuredPath, isWindows: true);

        Assert.Equal(
            [
                configuredPath + ".exe",
                configuredPath,
                configuredPath + ".dll"
            ],
            candidates);
    }

    [Fact]
    public void GetExecutableCandidates_WhenNonWindowsProfileKeepsExeCompatibility_ShouldPreferExtensionlessThenConfiguredThenDll()
    {
        var configuredPath = Path.Combine("runtime", "IIoT.Edge.Shell.exe");
        var extensionlessPath = Path.Combine("runtime", "IIoT.Edge.Shell");
        var dllPath = Path.Combine("runtime", "IIoT.Edge.Shell.dll");

        var candidates = ShellLaunchTargetResolver.GetExecutableCandidates(configuredPath, isWindows: false);

        Assert.Equal(
            [
                extensionlessPath,
                configuredPath,
                dllPath
            ],
            candidates);
    }

    [Fact]
    public void Resolve_WhenWindowsProfileUsesExtensionlessShellNameAndExeExists_ShouldLaunchExe()
    {
        var configuredPath = Path.Combine("runtime", "IIoT.Edge.Shell");
        var exePath = configuredPath + ".exe";

        var target = ShellLaunchTargetResolver.Resolve(
            configuredPath,
            isWindows: true,
            fileExists: path => string.Equals(path, exePath, StringComparison.Ordinal));

        Assert.Equal(exePath, target.FileName);
        Assert.Empty(target.Arguments);
        Assert.Equal(Path.GetDirectoryName(exePath), target.WorkingDirectory);
    }

    [Fact]
    public void Resolve_WhenOnlyDllExists_ShouldUseDotnetFallback()
    {
        var configuredPath = Path.Combine("runtime", "IIoT.Edge.Shell");
        var dllPath = configuredPath + ".dll";

        var target = ShellLaunchTargetResolver.Resolve(
            configuredPath,
            isWindows: false,
            fileExists: path => string.Equals(path, dllPath, StringComparison.Ordinal));

        Assert.Equal("dotnet", target.FileName);
        Assert.Equal(new[] { dllPath }, target.Arguments);
        Assert.Equal(Path.GetDirectoryName(dllPath), target.WorkingDirectory);
    }

    [Fact]
    public void Launch_ShouldSetMachineProfileEnvironmentVariable()
    {
        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-tests",
            Guid.NewGuid().ToString("N"),
            OperatingSystem.IsWindows() ? "IIoT.Edge.Shell.exe" : "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var starter = new SpyProcessStarter();
        var service = CreateService(starter);
        var profile = new LauncherProfileDefinition(
            "TestPluginLine",
            "测试插件",
            "TestPlugin profile",
            null,
            "TestPluginLine",
            executablePath,
            "BeakerOutline",
            "#4D7C0F");

        try
        {
            service.Launch(profile);

            Assert.NotNull(starter.StartInfo);
            Assert.Equal(executablePath, starter.StartInfo!.FileName);
            Assert.Equal("TestPluginLine", starter.StartInfo.EnvironmentVariables["Shell__MachineProfile"]);
            Assert.False(starter.StartInfo.UseShellExecute);
            Assert.True(service.HasAnyRunningShellProcess());
            Assert.True(service.IsProfileRunning(profile));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executablePath)!, recursive: true);
        }
    }

    [Fact]
    public void IsProfileRunning_WhenDifferentProfileIsTracked_ShouldReturnFalse()
    {
        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-tests",
            Guid.NewGuid().ToString("N"),
            OperatingSystem.IsWindows() ? "IIoT.Edge.Shell.exe" : "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var service = CreateService(new SpyProcessStarter());
        var alphaProfile = new LauncherProfileDefinition(
            "TestPluginAlphaLine",
            "测试插件甲",
            "Alpha profile",
            null,
            "TestPluginAlphaLine",
            executablePath,
            "ChartBar",
            "#2563EB");
        var betaProfile = alphaProfile with
        {
            ProfileId = "TestPluginBetaLine",
            DisplayName = "测试插件乙",
            MachineProfile = "TestPluginBetaLine"
        };

        try
        {
            service.Launch(alphaProfile);

            Assert.True(service.HasAnyRunningShellProcess());
            Assert.True(service.IsProfileRunning(alphaProfile));
            Assert.False(service.IsProfileRunning(betaProfile));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executablePath)!, recursive: true);
        }
    }

    [Fact]
    public void IsProfileRunning_WhenTrackedProcessMissingButProfileInstanceIsRunning_ShouldReturnTrue()
    {
        var profile = new LauncherProfileDefinition(
            "TestPluginAlphaLine",
            "测试插件甲",
            "AP profile",
            null,
            "TestPluginAlphaLine",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB");
        var service = CreateService(
            new SpyProcessStarter(),
            new FakeShellInstanceIdResolver(("TestPluginAlphaLine", "TestPluginAlphaLine")),
            new FakeShellInstanceProbe("TestPluginAlphaLine"));

        Assert.True(service.IsProfileRunning(profile));
    }

    [Fact]
    public void IsProfileRunning_WhenDifferentProfileInstanceIsRunning_ShouldReturnFalse()
    {
        var betaProfile = new LauncherProfileDefinition(
            "TestPluginBetaLine",
            "测试插件乙",
            "Beta profile",
            null,
            "TestPluginBetaLine",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB");
        var service = CreateService(
            new SpyProcessStarter(),
            new FakeShellInstanceIdResolver(("TestPluginBetaLine", "TestPluginBetaLine")),
            new FakeShellInstanceProbe("TestPluginAlphaLine"));

        Assert.False(service.IsProfileRunning(betaProfile));
    }

    [Fact]
    public void IsProfileRunning_WhenInstanceIdCannotBeResolved_ShouldReturnFalse()
    {
        var profile = new LauncherProfileDefinition(
            "TestPluginBetaLine",
            "测试插件乙",
            "CP profile",
            null,
            "TestPluginBetaLine",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB");
        var service = CreateService(
            new SpyProcessStarter(),
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe("TestPluginBetaLine"));

        Assert.False(service.IsProfileRunning(profile));
    }

    [Fact]
    public void ShellInstanceIdResolver_ShouldReadPackagedMachineProfileConfig()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"));
        var hostDirectory = Path.Combine(tempDirectory, "host");
        Directory.CreateDirectory(hostDirectory);
        var executablePath = Path.Combine(hostDirectory, "IIoT.Edge.Shell");
        File.WriteAllText(
            Path.Combine(hostDirectory, "appsettings.machine.TestPluginAlphaLine.json"),
            """
            {
              "InstanceId": "TestPluginAlphaLine"
            }
            """);
        var profile = new LauncherProfileDefinition(
            "TestPluginAlphaLine",
            "测试插件甲",
            "AP profile",
            null,
            "TestPluginAlphaLine",
            executablePath,
            "ChartBar",
            "#2563EB");

        try
        {
            var instanceId = new ShellInstanceIdResolver().ResolveInstanceId(profile);

            Assert.Equal("TestPluginAlphaLine", instanceId);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShellInstanceIdResolver_ShouldPreferProgramDataMachineProfileConfig()
    {
        var previousDataRoot = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"));
        var dataRoot = Path.Combine(tempDirectory, "data-root");
        var hostDirectory = Path.Combine(tempDirectory, "layout", "host");
        Directory.CreateDirectory(hostDirectory);
        var executablePath = Path.Combine(hostDirectory, "IIoT.Edge.Shell");
        File.WriteAllText(
            Path.Combine(hostDirectory, "appsettings.machine.TestPluginAlphaLine.json"),
            """
            {
              "InstanceId": "PackagedInstance"
            }
            """);
        Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        var externalConfigPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
            "TestPluginAlphaLine",
            hostDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(externalConfigPath)!);
        File.WriteAllText(
            externalConfigPath,
            """
            {
              "InstanceId": "ExternalInstance"
            }
            """);
        var profile = new LauncherProfileDefinition(
            "TestPluginAlphaLine",
            "测试插件甲",
            "AP profile",
            null,
            "TestPluginAlphaLine",
            executablePath,
            "ChartBar",
            "#2563EB");

        try
        {
            var instanceId = new ShellInstanceIdResolver().ResolveInstanceId(profile);

            Assert.Equal("ExternalInstance", instanceId);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                previousDataRoot);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ShellInstanceIdResolver_WhenMachineProfileConfigMissing_ShouldReturnNull()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"));
        var hostDirectory = Path.Combine(tempDirectory, "host");
        Directory.CreateDirectory(hostDirectory);
        var profile = new LauncherProfileDefinition(
            "MissingLine",
            "缺失工序",
            "Missing profile",
            null,
            "MissingLine",
            Path.Combine(hostDirectory, "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB");

        try
        {
            var instanceId = new ShellInstanceIdResolver().ResolveInstanceId(profile);

            Assert.Null(instanceId);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Launch_WhenOnlyShellDllExists_ShouldFallbackToDotnet()
    {
        var configuredPath = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"), "IIoT.Edge.Shell");
        var dllPath = configuredPath + ".dll";
        Directory.CreateDirectory(Path.GetDirectoryName(configuredPath)!);
        File.WriteAllText(dllPath, string.Empty);
        var starter = new SpyProcessStarter();
        var service = CreateService(starter);
        var profile = new LauncherProfileDefinition(
            "TestPluginLine",
            "测试插件",
            "TestPlugin profile",
            null,
            "TestPluginLine",
            configuredPath,
            "BeakerOutline",
            "#4D7C0F");

        try
        {
            service.Launch(profile);

            Assert.NotNull(starter.StartInfo);
            Assert.Equal("dotnet", starter.StartInfo!.FileName);
            Assert.Contains(dllPath, starter.StartInfo.ArgumentList);
            Assert.Equal(Path.GetDirectoryName(dllPath), starter.StartInfo.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(configuredPath)!, recursive: true);
        }
    }

    [Fact]
    public void Launch_WhenDotnetChildSignalsReady_ShouldHoldGateUntilPersistentShellPresenceExists()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-handoff-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        var configuredPath = Path.Combine(
            baseDirectory,
            "runtime",
            "IIoT.Edge.Shell");
        var dllPath = configuredPath + ".dll";
        Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
        File.WriteAllText(dllPath, string.Empty);
        var gate = new FileLauncherUpdateOperationGate(baseDirectory);
        using var starter = new ReadyShellProcessStarter(baseDirectory);
        using var service = CreateService(
            starter,
            updateOperationGate: gate);
        var profile = new LauncherProfileDefinition(
            "TestPluginLine",
            "测试插件",
            "TestPlugin profile",
            null,
            "TestPluginLine",
            configuredPath,
            "BeakerOutline",
            "#4D7C0F");

        try
        {
            service.Launch(profile);

            Assert.NotNull(starter.StartInfo);
            Assert.Equal("dotnet", starter.StartInfo!.FileName);
            Assert.True(starter.SignaledReady);
            Assert.Null(gate.TryAcquireUpdate());

            starter.ReleaseShellPresence();
            using var updateAfterShellExit = gate.TryAcquireUpdate();
            Assert.NotNull(updateAfterShellExit);
        }
        finally
        {
            if (Directory.Exists(
                    Directory.GetParent(baseDirectory)?.FullName))
            {
                Directory.Delete(
                    Directory.GetParent(baseDirectory)!.FullName,
                    recursive: true);
            }
        }
    }

    private static ShellLaunchService CreateService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver? instanceIdResolver = null,
        IShellInstanceProbe? instanceProbe = null,
        ILauncherUpdateOperationGate? updateOperationGate = null,
        IEdgeUpdateTransactionRecovery? updateTransactionRecovery = null)
        => new(
            processStarter,
            instanceIdResolver ?? new FakeShellInstanceIdResolver(),
            instanceProbe ?? new FakeShellInstanceProbe(),
            updateOperationGate,
            updateTransactionRecovery);

    private sealed class SpyProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return Process.GetCurrentProcess();
        }
    }

    private sealed class ReadyShellProcessStarter(
        string baseDirectory) : IProcessStarter, IDisposable
    {
        private IDisposable? _shellPresence;

        public ProcessStartInfo? StartInfo { get; private set; }

        public bool SignaledReady { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            _shellPresence =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    baseDirectory);
            Assert.NotNull(_shellPresence);
            var readyPath = startInfo.EnvironmentVariables[
                EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable];
            Assert.False(string.IsNullOrWhiteSpace(readyPath));
            File.WriteAllText(readyPath!, "ready");
            SignaledReady = true;
            return Process.GetCurrentProcess();
        }

        public void ReleaseShellPresence()
            => Interlocked.Exchange(ref _shellPresence, null)?.Dispose();

        public void Dispose() => ReleaseShellPresence();
    }

    private sealed class FakeShellInstanceIdResolver(params (string MachineProfile, string InstanceId)[] mappings)
        : IShellInstanceIdResolver
    {
        private readonly Dictionary<string, string> _mappings = mappings.ToDictionary(
            static mapping => mapping.MachineProfile,
            static mapping => mapping.InstanceId,
            StringComparer.OrdinalIgnoreCase);

        public string? ResolveInstanceId(LauncherProfileDefinition profile)
            => _mappings.TryGetValue(profile.MachineProfile, out var instanceId)
                ? instanceId
                : null;
    }

    private sealed class FakeShellInstanceProbe(params string[] runningInstanceIds) : IShellInstanceProbe
    {
        private readonly HashSet<string> _runningInstanceIds = new(runningInstanceIds, StringComparer.OrdinalIgnoreCase);

        public bool IsInstanceRunning(string instanceId) => _runningInstanceIds.Contains(instanceId);
    }

    private sealed class BlockedUpdateTransactionRecovery(string blockedProfile)
        : IEdgeUpdateTransactionRecovery
    {
        public EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction()
            => new(false, false, true, "blocked");

        public bool IsProfileBlocked(string machineProfile)
            => string.Equals(
                blockedProfile,
                machineProfile,
                StringComparison.OrdinalIgnoreCase);
    }
}
