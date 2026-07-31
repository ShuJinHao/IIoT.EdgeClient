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
    public void Constructor_WhenGateOrRecoveryIsMissing_ShouldFailClosed()
    {
        var starter = new SpyProcessStarter(signalReady: false);
        var instanceIdResolver = new FakeShellInstanceIdResolver();
        var instanceProbe = new FakeShellInstanceProbe();
        var gate = new TestLauncherUpdateOperationGate();
        var recovery = UnblockedUpdateTransactionRecovery.Instance;

        Assert.Throws<ArgumentNullException>(() => new ShellLaunchService(
            starter,
            instanceIdResolver,
            instanceProbe,
            null!,
            recovery));
        Assert.Throws<ArgumentNullException>(() => new ShellLaunchService(
            starter,
            instanceIdResolver,
            instanceProbe,
            gate,
            null!));
    }

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
    public async Task Launch_WhenUpdateLeaseIsHeld_ShouldRejectBeforeStartingProcess()
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

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAsync(
                    profile,
                    TestContext.Current.CancellationToken));

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
    public async Task Launch_WhenProfileIsBlockedByFailedRecovery_ShouldRejectBeforeStartingProcess()
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken));

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
    public async Task Launch_ShouldSetMachineProfileEnvironmentVariable()
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
            await service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);

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
    public async Task IsProfileRunning_WhenDifferentProfileIsTracked_ShouldReturnFalse()
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
            await service.LaunchAsync(
                alphaProfile,
                TestContext.Current.CancellationToken);

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
    public async Task Launch_WhenOnlyShellDllExists_ShouldFallbackToDotnet()
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
            await service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);

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
    public async Task Launch_WhenDotnetChildSignalsReady_ShouldHoldGateUntilPersistentShellPresenceExists()
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
            await service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task Launch_WhenReadyOutcomeMissesExpectedModule_ShouldTerminateIncompleteChild()
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
        using var starter = new ReadyShellProcessStarter(
            baseDirectory,
            activeModuleIds: []);
        var terminated = false;
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            gate,
            updateTransactionRecovery: UnblockedUpdateTransactionRecovery.Instance,
            terminateProcess: _ => terminated = true);
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            configuredPath,
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAsync(
                    profile,
                    TestContext.Current.CancellationToken));

            Assert.Contains("未激活目标模块 AP", exception.Message, StringComparison.Ordinal);
            Assert.True(terminated);
            Assert.False(service.HasAnyRunningShellProcess());
        }
        finally
        {
            starter.ReleaseShellPresence();
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Launch_WhenShellReportsDiagnosticFailure_ShouldKeepRepairWindowTracked()
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
        using var starter = new ReadyShellProcessStarter(
            baseDirectory,
            status: EdgeClientShellLaunchStatuses.Failed,
            message: "AP 插件未能加载。");
        var terminated = false;
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            gate,
            updateTransactionRecovery: UnblockedUpdateTransactionRecovery.Instance,
            terminateProcess: _ => terminated = true);
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            configuredPath,
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAsync(
                    profile,
                    TestContext.Current.CancellationToken));

            Assert.Contains("受控启动失败", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("AP 插件未能加载", exception.Message, StringComparison.Ordinal);
            Assert.False(terminated);
            Assert.True(service.HasAnyRunningShellProcess());
            Assert.Null(gate.TryAcquireUpdate());
        }
        finally
        {
            starter.ReleaseShellPresence();
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Launch_WhenLifecycleIsStillStarting_ShouldRemainAsynchronousUntilOutcomeArrives()
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
        using var starter = new DeferredReadyShellProcessStarter(
            baseDirectory,
            ["AP"]);
        using var service = CreateService(
            starter,
            updateOperationGate: gate);
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            configuredPath,
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        try
        {
            var launchTask = service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);
            Assert.False(launchTask.IsCompleted);

            starter.SignalReady();
            await launchTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.True(service.IsProfileRunning(profile));
        }
        finally
        {
            starter.ReleaseShellPresence();
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Launch_WhenReadySignalBelongsToAnotherProcess_ShouldTerminateAndReject()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-identity-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        var configuredPath = Path.Combine(baseDirectory, "runtime", "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(configuredPath)!);
        File.WriteAllText(configuredPath + ".dll", string.Empty);
        using var starter = new ReadyShellProcessStarter(
            baseDirectory,
            activeModuleIds: ["AP"],
            processIdDelta: 1);
        var terminated = false;
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            new FileLauncherUpdateOperationGate(baseDirectory),
            UnblockedUpdateTransactionRecovery.Instance,
            terminateProcess: _ => terminated = true);
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            configuredPath,
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.LaunchAsync(profile, TestContext.Current.CancellationToken));

            Assert.Contains("不匹配的进程身份", exception.Message, StringComparison.Ordinal);
            Assert.True(terminated);
            Assert.False(service.HasAnyRunningShellProcess());
        }
        finally
        {
            starter.ReleaseShellPresence();
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Launch_WhenShellIsReadyWithDiagnostics_ShouldReturnStableDiagnosticResult()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-diagnostic-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        var configuredPath = Path.Combine(baseDirectory, "runtime", "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(configuredPath)!);
        File.WriteAllText(configuredPath + ".dll", string.Empty);
        var expectedDiagnostic = new EdgeClientShellLaunchDiagnostic(
            "STARTUP_MES_RETRY_TASK_FAILED",
            "System.Diagnostics",
            "AP");
        using var starter = new ReadyShellProcessStarter(
            baseDirectory,
            status: EdgeClientShellLaunchStatuses.ReadyWithDiagnostics,
            activeModuleIds: ["AP"],
            diagnostics: [expectedDiagnostic]);
        using var service = CreateService(
            starter,
            updateOperationGate: new FileLauncherUpdateOperationGate(baseDirectory));
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            configuredPath,
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        try
        {
            var result = await service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);

            Assert.True(result.ReadyWithDiagnostics);
            Assert.Equal(expectedDiagnostic, Assert.Single(result.Diagnostics));
            Assert.True(service.IsProfileRunning(profile));
        }
        finally
        {
            starter.ReleaseShellPresence();
            var root = Directory.GetParent(baseDirectory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Launch_WhenExpectedModuleIsNotSelected_ShouldFailBeforeProcessStart()
    {
        var starter = new SpyProcessStarter();
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            new TestLauncherUpdateOperationGate(),
            UnblockedUpdateTransactionRecovery.Instance,
            new StubEnabledPluginSelectionSource(true, ["CP"]));
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAsync(profile, TestContext.Current.CancellationToken));

        Assert.Contains("不在当前启用工序清单", exception.Message, StringComparison.Ordinal);
        Assert.Null(starter.StartInfo);
    }

    [Fact]
    public async Task Launch_WhenSelectionChangesBeforeLease_ShouldRevalidateInsideLease()
    {
        var starter = new SpyProcessStarter();
        var selection = new MutableEnabledPluginSelectionSource(["AP"]);
        var gate = new MutatingLaunchGate(() => selection.SetModules(["CP"]));
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            gate,
            UnblockedUpdateTransactionRecovery.Instance,
            selection);
        var profile = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            Path.Combine(Path.GetTempPath(), "IIoT.Edge.Shell"),
            "ChartBar",
            "#2563EB")
        {
            ExpectedModuleIds = ["AP"]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LaunchAsync(profile, TestContext.Current.CancellationToken));

        Assert.Contains("不在当前启用工序清单", exception.Message, StringComparison.Ordinal);
        Assert.Null(starter.StartInfo);
        Assert.Equal(1, selection.LoadCount);
    }

    [Fact]
    public async Task Launch_WhenChildNeverSignalsReady_ShouldCancelAndReleaseLaunchGate()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-handoff-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        var executablePath = Path.Combine(
            baseDirectory,
            "runtime",
            OperatingSystem.IsWindows()
                ? "IIoT.Edge.Shell.exe"
                : "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var gate = new FileLauncherUpdateOperationGate(baseDirectory);
        var starter = new SpyProcessStarter(signalReady: false);
        var terminated = false;
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            gate,
            updateTransactionRecovery: UnblockedUpdateTransactionRecovery.Instance,
            terminateProcess: _ => terminated = true);
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
#pragma warning disable xUnit1051 // This test verifies caller-driven cancellation before the test token fires.
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.LaunchAsync(profile, cancellation.Token));
#pragma warning restore xUnit1051

            Assert.True(terminated);
            Assert.False(service.HasAnyRunningShellProcess());
            using var updateAfterFailedLaunch = gate.TryAcquireUpdate();
            Assert.NotNull(updateAfterFailedLaunch);
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
    public async Task Launch_WhenChildNeverSignalsReady_ShouldReachDeadlineAndReleaseLaunchGate()
    {
        var baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-shell-handoff-tests",
            Guid.NewGuid().ToString("N"),
            "launcher");
        var executablePath = Path.Combine(
            baseDirectory,
            "runtime",
            OperatingSystem.IsWindows()
                ? "IIoT.Edge.Shell.exe"
                : "IIoT.Edge.Shell");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var gate = new FileLauncherUpdateOperationGate(baseDirectory);
        var starter = new SpyProcessStarter(signalReady: false);
        var deadline = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = false;
        using var service = new ShellLaunchService(
            starter,
            new FakeShellInstanceIdResolver(),
            new FakeShellInstanceProbe(),
            gate,
            updateTransactionRecovery: UnblockedUpdateTransactionRecovery.Instance,
            terminateProcess: _ => terminated = true,
            readinessDeadline: _ => deadline.Task);
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
            var launchTask = service.LaunchAsync(
                profile,
                TestContext.Current.CancellationToken);
            Assert.False(launchTask.IsCompleted);

            deadline.SetResult();
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => launchTask);

            Assert.Contains("未在允许的启动窗口内完成就绪握手", exception.Message, StringComparison.Ordinal);
            Assert.True(terminated);
            Assert.False(service.HasAnyRunningShellProcess());
            using var updateAfterFailedLaunch = gate.TryAcquireUpdate();
            Assert.NotNull(updateAfterFailedLaunch);
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
            updateOperationGate ?? new TestLauncherUpdateOperationGate(),
            updateTransactionRecovery ?? UnblockedUpdateTransactionRecovery.Instance);

    private sealed class SpyProcessStarter(bool signalReady = true) : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            var process = Process.GetCurrentProcess();
            var readyPath = startInfo.EnvironmentVariables[
                EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable];
            var machineProfile = startInfo.EnvironmentVariables["Shell__MachineProfile"];
            if (signalReady
                && !string.IsNullOrWhiteSpace(readyPath)
                && !string.IsNullOrWhiteSpace(machineProfile))
            {
                Assert.True(EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    readyPath,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        EdgeClientShellLaunchStatuses.Ready,
                        machineProfile,
                        [],
                        null)
                    {
                        ProcessId = process.Id
                    }));
            }

            return process;
        }
    }

    private sealed class ReadyShellProcessStarter(
        string baseDirectory,
        string status = EdgeClientShellLaunchStatuses.Ready,
        IReadOnlyList<string>? activeModuleIds = null,
        string? machineProfileOverride = null,
        string? message = null,
        int processIdDelta = 0,
        IReadOnlyList<EdgeClientShellLaunchDiagnostic>? diagnostics = null) : IProcessStarter, IDisposable
    {
        private IDisposable? _shellPresence;

        public ProcessStartInfo? StartInfo { get; private set; }

        public bool SignaledReady { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            var process = Process.GetCurrentProcess();
            _shellPresence =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    baseDirectory);
            Assert.NotNull(_shellPresence);
            var readyPath = startInfo.EnvironmentVariables[
                EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable];
            Assert.False(string.IsNullOrWhiteSpace(readyPath));
            var machineProfile = machineProfileOverride
                                 ?? startInfo.EnvironmentVariables[
                                     "Shell__MachineProfile"]
                                 ?? "Default";
            SignaledReady =
                EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    readyPath!,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        status,
                        machineProfile,
                        activeModuleIds ?? [],
                        status == EdgeClientShellLaunchStatuses.Failed
                            ? message ?? "启动诊断失败。"
                            : null)
                    {
                        ProcessId = process.Id + processIdDelta,
                        Diagnostics = diagnostics ?? []
                    },
                    baseDirectory);
            Assert.True(SignaledReady);
            return process;
        }

        public void ReleaseShellPresence()
            => Interlocked.Exchange(ref _shellPresence, null)?.Dispose();

        public void Dispose() => ReleaseShellPresence();
    }

    private sealed class DeferredReadyShellProcessStarter(
        string baseDirectory,
        IReadOnlyList<string> activeModuleIds) : IProcessStarter, IDisposable
    {
        private IDisposable? _shellPresence;
        private string? _machineProfile;
        private string? _readyPath;
        private int _processId;

        public Process? Start(ProcessStartInfo startInfo)
        {
            _shellPresence =
                EdgeClientUpdateCoordination.TryAcquireShellPresence(
                    baseDirectory);
            Assert.NotNull(_shellPresence);
            _readyPath = startInfo.EnvironmentVariables[
                EdgeClientUpdateCoordination.ShellLaunchReadyEnvironmentVariable];
            _machineProfile = startInfo.EnvironmentVariables[
                "Shell__MachineProfile"];
            Assert.False(string.IsNullOrWhiteSpace(_readyPath));
            Assert.False(string.IsNullOrWhiteSpace(_machineProfile));
            var process = Process.GetCurrentProcess();
            _processId = process.Id;
            return process;
        }

        public void SignalReady()
        {
            Assert.True(
                EdgeClientUpdateCoordination.TryWriteShellLaunchOutcomeToPath(
                    _readyPath!,
                    new EdgeClientShellLaunchOutcome(
                        EdgeClientUpdateCoordination.ShellLaunchOutcomeSchemaVersion,
                        EdgeClientShellLaunchStatuses.Ready,
                        _machineProfile!,
                        activeModuleIds,
                        Message: null)
                    {
                        ProcessId = _processId
                    },
                    baseDirectory));
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

    private sealed class StubEnabledPluginSelectionSource(
        bool manifestIsValid,
        IReadOnlyList<string> moduleIds)
        : ILauncherEnabledPluginSelectionSource
    {
        public LauncherEnabledPluginSelection Load()
            => new(
                manifestIsValid,
                moduleIds
                    .Select(static moduleId => new LauncherEnabledPluginSelectionItem(
                        moduleId,
                        moduleId))
                    .ToArray());
    }

    private sealed class MutableEnabledPluginSelectionSource(
        IReadOnlyList<string> moduleIds)
        : ILauncherEnabledPluginSelectionSource
    {
        private IReadOnlyList<string> _moduleIds = moduleIds;
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public void SetModules(IReadOnlyList<string> value) => _moduleIds = value;

        public LauncherEnabledPluginSelection Load()
        {
            Interlocked.Increment(ref _loadCount);
            return new LauncherEnabledPluginSelection(
                true,
                _moduleIds
                    .Select(static moduleId => new LauncherEnabledPluginSelectionItem(
                        moduleId,
                        moduleId))
                    .ToArray());
        }
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

    private sealed class UnblockedUpdateTransactionRecovery : IEdgeUpdateTransactionRecovery
    {
        public static UnblockedUpdateTransactionRecovery Instance { get; } = new();

        public EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction()
            => new(false, false, false);

        public bool IsProfileBlocked(string machineProfile) => false;
    }

    private sealed class TestLauncherUpdateOperationGate : ILauncherUpdateOperationGate
    {
        public IDisposable TryAcquire() => Lease.Instance;

        public IDisposable TryAcquireUpdate() => Lease.Instance;

        public string CreateShellLaunchReadyPath()
            => EdgeClientUpdateCoordination.CreateShellLaunchReadyPath();

        private sealed class Lease : IDisposable
        {
            public static Lease Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class MutatingLaunchGate(Action beforeAcquire)
        : ILauncherUpdateOperationGate
    {
        public IDisposable TryAcquire()
        {
            beforeAcquire();
            return Lease.Instance;
        }

        public IDisposable TryAcquireUpdate() => Lease.Instance;

        public string CreateShellLaunchReadyPath()
            => EdgeClientUpdateCoordination.CreateShellLaunchReadyPath();

        private sealed class Lease : IDisposable
        {
            public static Lease Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
