using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using System.Diagnostics;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class ShellLaunchServiceTests
{
    [Fact]
    public void Launch_ShouldSetMachineProfileEnvironmentVariable()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), "edge-launcher-shell-tests", Guid.NewGuid().ToString("N"), "IIoT.Edge.Shell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        File.WriteAllText(executablePath, string.Empty);
        var starter = new SpyProcessStarter();
        var service = new ShellLaunchService(starter);
        var profile = new LauncherProfileDefinition(
            "StackingLine",
            "叠片",
            "Stacking profile",
            null,
            "StackingLine",
            executablePath,
            "LayersTriple",
            "#0F766E");

        try
        {
            service.Launch(profile);

            Assert.NotNull(starter.StartInfo);
            Assert.Equal(executablePath, starter.StartInfo!.FileName);
            Assert.Equal("StackingLine", starter.StartInfo.EnvironmentVariables["Shell__MachineProfile"]);
            Assert.False(starter.StartInfo.UseShellExecute);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executablePath)!, recursive: true);
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
