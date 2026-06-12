using IIoT.Edge.SharedKernel.Configuration;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class RuntimeLayoutSyncExternalPluginBehaviorTests
{
    [Fact]
    public void Run_WhenRuntimeLayoutIsRefreshed_ShouldPublishSingleHostAndPreserveDataDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        var originalDataRoot = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            var repoRoot = Path.Combine(tempDirectory, "repo");
            var layoutRoot = Path.Combine(tempDirectory, "layout");
            var launcherRuntimeRoot = Path.Combine(layoutRoot, "launcher");
            var shellRuntimeRoot = Path.Combine(tempDirectory, "shell-build");
            var dataRoot = Path.Combine(tempDirectory, "program-data");
            var hostRoot = Path.Combine(layoutRoot, "host");
            var pluginsRoot = Path.Combine(layoutRoot, "plugins");
            var dataSentinelPath = Path.Combine(layoutRoot, "data", "sentinel.txt");

            Directory.CreateDirectory(launcherRuntimeRoot);
            Directory.CreateDirectory(shellRuntimeRoot);
            WriteText(Path.Combine(shellRuntimeRoot, "IIoT.Edge.Shell"), string.Empty);
            WriteText(Path.Combine(shellRuntimeRoot, "IIoT.Edge.Shell.dll"), string.Empty);
            WriteText(Path.Combine(hostRoot, "stale.txt"), "old host content");
            WriteText(Path.Combine(hostRoot, "Modules", "Homogenization", "plugin.json"), "{}");
            WriteText(dataSentinelPath, "runtime data content");
            WriteText(Path.Combine(repoRoot, "config", "appsettings.machine.HomogenizationLine.json"), "{}");
            WriteText(
                Path.Combine(repoRoot, "scripts", "edge-runtime.publish.json"),
                $$"""
                {
                  "schemaVersion": 2,
                  "launcherDirectory": "launcher",
                  "hostDirectory": "host",
                  "pluginsRoot": "plugins",
                  "profiles": [
                    {
                      "profileId": "HomogenizationLine",
                      "machineProfile": "HomogenizationLine",
                      "machineConfig": "config/appsettings.machine.HomogenizationLine.json",
                      "moduleIds": [ "Homogenization" ]
                    }
                  ]
                }
                """);
            WriteText(
                Path.Combine(repoRoot, "launcher.profiles.json"),
                $$"""
                [
                  {
                    "profileId": "HomogenizationLine",
                    "displayName": "Homogenization",
                    "machineProfile": "HomogenizationLine",
                    "executablePath": "../host/IIoT.Edge.Shell"
                  }
                ]
                """);

            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);

            var fileSystem = new RuntimeLayoutSyncFileSystem();
            var app = new RuntimeLayoutSyncApp(
                fileSystem,
                new RuntimeLayoutSyncValidation(fileSystem),
                new StubRuntimeLayoutSyncModulePublisher());

            app.Run(new CommandLineOptions(
                "Debug",
                repoRoot,
                "scripts/edge-runtime.publish.json",
                "launcher.profiles.json",
                layoutRoot,
                launcherRuntimeRoot,
                shellRuntimeRoot));

            Assert.False(File.Exists(Path.Combine(hostRoot, "stale.txt")));
            Assert.False(Directory.Exists(Path.Combine(hostRoot, "Modules")));
            Assert.True(File.Exists(Path.Combine(hostRoot, "IIoT.Edge.Shell")));
            Assert.True(File.Exists(Path.Combine(pluginsRoot, "Homogenization", "plugin.json")));
            Assert.True(File.Exists(dataSentinelPath));
            Assert.Equal("runtime data content", File.ReadAllText(dataSentinelPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                originalDataRoot);
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Run_WhenShellSourceIsHostDirectory_ShouldNotCleanHostOutput()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var repoRoot = Path.Combine(tempDirectory, "repo");
            var layoutRoot = Path.Combine(tempDirectory, "layout");
            var launcherRuntimeRoot = Path.Combine(layoutRoot, "launcher");
            var hostRoot = Path.Combine(layoutRoot, "host");
            var pluginsRoot = Path.Combine(layoutRoot, "plugins");
            var hostSentinelPath = Path.Combine(hostRoot, "host-runtime-file.dll");

            Directory.CreateDirectory(launcherRuntimeRoot);
            Directory.CreateDirectory(hostRoot);
            WriteText(Path.Combine(hostRoot, "IIoT.Edge.Shell.dll"), string.Empty);
            WriteText(hostSentinelPath, "host content");
            WriteText(Path.Combine(hostRoot, "Modules", "Homogenization", "plugin.json"), "{}");
            WriteText(Path.Combine(repoRoot, "config", "appsettings.machine.HomogenizationLine.json"), "{}");
            WriteText(
                Path.Combine(repoRoot, "scripts", "edge-runtime.publish.json"),
                $$"""
                {
                  "schemaVersion": 2,
                  "launcherDirectory": "launcher",
                  "hostDirectory": "host",
                  "pluginsRoot": "plugins",
                  "profiles": [
                    {
                      "profileId": "HomogenizationLine",
                      "machineProfile": "HomogenizationLine",
                      "machineConfig": "config/appsettings.machine.HomogenizationLine.json",
                      "moduleIds": [ "Homogenization" ]
                    }
                  ]
                }
                """);
            WriteText(
                Path.Combine(repoRoot, "launcher.profiles.json"),
                $$"""
                [
                  {
                    "profileId": "HomogenizationLine",
                    "displayName": "Homogenization",
                    "machineProfile": "HomogenizationLine",
                    "executablePath": "../host/IIoT.Edge.Shell"
                  }
                ]
                """);

            var fileSystem = new RuntimeLayoutSyncFileSystem();
            var app = new RuntimeLayoutSyncApp(
                fileSystem,
                new RuntimeLayoutSyncValidation(fileSystem),
                new StubRuntimeLayoutSyncModulePublisher());

            app.Run(new CommandLineOptions(
                "Debug",
                repoRoot,
                "scripts/edge-runtime.publish.json",
                "launcher.profiles.json",
                layoutRoot,
                launcherRuntimeRoot,
                hostRoot));

            Assert.True(File.Exists(hostSentinelPath));
            Assert.Equal("host content", File.ReadAllText(hostSentinelPath));
            Assert.False(Directory.Exists(Path.Combine(hostRoot, "Modules")));
            Assert.True(File.Exists(Path.Combine(hostRoot, "appsettings.machine.HomogenizationLine.json")));
            Assert.True(File.Exists(Path.Combine(pluginsRoot, "Homogenization", "plugin.json")));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private sealed class StubRuntimeLayoutSyncModulePublisher : IRuntimeLayoutSyncModulePublisher
    {
        public void PublishModulesToPluginsRoot(
            string repoRoot,
            string configuration,
            IReadOnlyList<string> moduleIds,
            string targetPluginsRoot)
        {
            foreach (var moduleId in moduleIds)
            {
                WriteText(Path.Combine(targetPluginsRoot, moduleId, "plugin.json"), "{}");
            }
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-runtime-layout-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
