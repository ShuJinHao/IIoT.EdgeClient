using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
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
        var service = new ShellLaunchService(starter);
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
        var service = new ShellLaunchService(new SpyProcessStarter());
        var anode = new LauncherProfileDefinition(
            "DieCuttingAnodeLine",
            "负极模切",
            "AP profile",
            null,
            "DieCuttingAnodeLine",
            executablePath,
            "ChartBar",
            "#2563EB");
        var cathode = anode with
        {
            ProfileId = "DieCuttingCathodeLine",
            DisplayName = "正极模切",
            MachineProfile = "DieCuttingCathodeLine"
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
    public void Launch_WhenOnlyShellDllExists_ShouldFallbackToDotnet()
    {
        var configuredPath = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"), "IIoT.Edge.Shell");
        var dllPath = configuredPath + ".dll";
        Directory.CreateDirectory(Path.GetDirectoryName(configuredPath)!);
        File.WriteAllText(dllPath, string.Empty);
        var starter = new SpyProcessStarter();
        var service = new ShellLaunchService(starter);
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

    private sealed class SpyProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return Process.GetCurrentProcess();
        }
    }
}
