using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class ShellLaunchServiceTests
{
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
            "HomogenizationLine",
            "匀浆",
            "Homogenization profile",
            null,
            "HomogenizationLine",
            executablePath,
            "BeakerOutline",
            "#4D7C0F");

        try
        {
            service.Launch(profile);

            Assert.NotNull(starter.StartInfo);
            Assert.Equal(executablePath, starter.StartInfo!.FileName);
            Assert.Equal("HomogenizationLine", starter.StartInfo.EnvironmentVariables["Shell__MachineProfile"]);
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
        var anode = new LauncherProfileDefinition(
            "TestPluginAlphaLine",
            "测试插件甲",
            "AP profile",
            null,
            "TestPluginAlphaLine",
            executablePath,
            "ChartBar",
            "#2563EB");
        var cathode = anode with
        {
            ProfileId = "TestPluginBetaLine",
            DisplayName = "测试插件乙",
            MachineProfile = "TestPluginBetaLine"
        };

        try
        {
            service.Launch(anode);

            Assert.True(service.HasAnyRunningShellProcess());
            Assert.True(service.IsProfileRunning(anode));
            Assert.False(service.IsProfileRunning(cathode));
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
        var cathode = new LauncherProfileDefinition(
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
            new FakeShellInstanceIdResolver(("TestPluginBetaLine", "TestPluginBetaLine")),
            new FakeShellInstanceProbe("TestPluginAlphaLine"));

        Assert.False(service.IsProfileRunning(cathode));
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
            "HomogenizationLine",
            "匀浆",
            "Homogenization profile",
            null,
            "HomogenizationLine",
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

    private static ShellLaunchService CreateService(
        IProcessStarter processStarter,
        IShellInstanceIdResolver? instanceIdResolver = null,
        IShellInstanceProbe? instanceProbe = null)
        => new(
            processStarter,
            instanceIdResolver ?? new FakeShellInstanceIdResolver(),
            instanceProbe ?? new FakeShellInstanceProbe());

    private sealed class SpyProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return Process.GetCurrentProcess();
        }
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
}
