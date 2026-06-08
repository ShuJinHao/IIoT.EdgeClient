using IIoT.Edge.SharedKernel.Configuration;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class RuntimeLayoutSyncExternalPluginBehaviorTests
{
    [Fact]
    public void Run_WhenRuntimeLayoutIsRefreshed_ShouldPreserveExternalProfilePluginDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        var originalDataRoot = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            var repoRoot = Path.Combine(tempDirectory, "repo");
            var layoutRoot = Path.Combine(tempDirectory, "layout");
            var launcherRuntimeRoot = Path.Combine(tempDirectory, "launcher-build");
            var shellRuntimeRoot = Path.Combine(tempDirectory, "shell-build");
            var dataRoot = Path.Combine(tempDirectory, "program-data");
            var runtimeOutputDirectory = Path.Combine("runtimes", "homogenization");
            var runtimeRoot = Path.Combine(layoutRoot, runtimeOutputDirectory);
            var shellPath = Path.Combine(runtimeRoot, "IIoT.Edge.Shell");
            var profileExecutablePath = Path.GetRelativePath(launcherRuntimeRoot, shellPath);

            Directory.CreateDirectory(launcherRuntimeRoot);
            Directory.CreateDirectory(shellRuntimeRoot);
            WriteText(Path.Combine(shellRuntimeRoot, "IIoT.Edge.Shell"), string.Empty);
            WriteText(Path.Combine(shellRuntimeRoot, "IIoT.Edge.Shell.dll"), string.Empty);
            WriteText(Path.Combine(runtimeRoot, "stale.txt"), "old runtime content");
            WriteText(Path.Combine(repoRoot, "config", "appsettings.machine.HomogenizationLine.json"), "{}");
            WriteText(
                Path.Combine(repoRoot, "scripts", "edge-runtime.publish.json"),
                $$"""
                {
                  "launcherDirectory": "launcher",
                  "runtimes": [
                    {
                      "runtimeId": "homogenization",
                      "profileId": "homogenization",
                      "machineProfile": "HomogenizationLine",
                      "outputDirectory": "{{runtimeOutputDirectory.Replace('\\', '/')}}",
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
                    "profileId": "homogenization",
                    "displayName": "Homogenization",
                    "machineProfile": "HomogenizationLine",
                    "executablePath": "{{profileExecutablePath.Replace('\\', '/')}}"
                  }
                ]
                """);

            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
            var externalPluginCurrentDirectory = EdgeClientProgramDataPaths.ResolveProfilePluginCurrentDirectory(
                "HomogenizationLine",
                "Homogenization",
                runtimeRoot);
            var sentinelPath = Path.Combine(externalPluginCurrentDirectory, "sentinel.txt");
            WriteText(sentinelPath, "external plugin content");

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

            Assert.False(File.Exists(Path.Combine(runtimeRoot, "stale.txt")));
            Assert.True(File.Exists(shellPath));
            Assert.True(File.Exists(Path.Combine(runtimeRoot, "Modules", "Homogenization", "plugin.json")));
            Assert.True(File.Exists(sentinelPath));
            Assert.Equal("external plugin content", File.ReadAllText(sentinelPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                originalDataRoot);
            DeleteDirectory(tempDirectory);
        }
    }

    private sealed class StubRuntimeLayoutSyncModulePublisher : IRuntimeLayoutSyncModulePublisher
    {
        public void PublishModulesToRuntimeRoot(
            string repoRoot,
            string configuration,
            IReadOnlyList<string> moduleIds,
            string targetModulesRoot)
        {
            foreach (var moduleId in moduleIds)
            {
                WriteText(Path.Combine(targetModulesRoot, moduleId, "plugin.json"), "{}");
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
