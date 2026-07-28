using System.Text;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherProfileVisibilityServiceTests
{
    [Fact]
    public void SelectVisibleProfiles_ShouldUseInstallerEnabledPluginsManifest()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "current");
            Directory.CreateDirectory(currentDirectory);
            var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentDirectory);
            Directory.CreateDirectory(launcherDirectory);
            WriteText(
                Path.Combine(launcherDirectory, "iiot-enabled-plugins.json"),
                """
                {
                  "plugins": [
                    { "moduleId": "TestPluginAlpha" },
                    { "moduleId": "TestPluginBeta" }
                  ]
                }
                """);

            var profiles = CreateProcessProfiles(currentDirectory);
            var service = new LauncherProfileVisibilityService(
                currentDirectory,
                CreateModuleConfiguration(),
                new LauncherUpdateTargetFactory());

            var visible = service.SelectVisibleProfiles(profiles);
            var selection = service.ResolveSelection(profiles);

            Assert.Equal(
                ["TestPluginAlphaLine", "TestPluginBetaLine"],
                visible.Select(static profile => profile.ProfileId).OrderBy(static x => x).ToArray());
            Assert.Equal(
                ["TestPluginAlpha", "TestPluginBeta"],
                selection.EnabledModuleIds.OrderBy(static x => x).ToArray());
            Assert.Equal("TestPluginAlphaLine", selection.ModuleProfileIds["TestPluginAlpha"]);
            Assert.Equal("TestPluginBetaLine", selection.ModuleProfileIds["TestPluginBeta"]);
            Assert.Equal(
                ["TestPluginAlpha"],
                visible.Single(static profile => profile.ProfileId == "TestPluginAlphaLine").ExpectedModuleIds);
            Assert.Equal(
                ["TestPluginBeta"],
                selection.VisibleProfiles
                    .Single(static profile => profile.ProfileId == "TestPluginBetaLine")
                    .ExpectedModuleIds);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void SelectVisibleProfiles_ShouldFailClosedToMaintenanceProfileWhenManifestIsMissing()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "current");
            Directory.CreateDirectory(currentDirectory);
            var profiles = CreateProcessProfiles(currentDirectory);
            var service = new LauncherProfileVisibilityService(
                currentDirectory,
                CreateModuleConfiguration(),
                new LauncherUpdateTargetFactory());

            var visible = service.SelectVisibleProfiles(profiles);

            Assert.Equal(["Default"], visible.Select(static profile => profile.ProfileId).ToArray());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void SelectVisibleProfiles_WhenNoPluginSignalExists_ShouldReturnMaintenanceProfileOnly()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "current");
            Directory.CreateDirectory(currentDirectory);
            var profiles = CreateProcessProfiles(currentDirectory);
            var service = new LauncherProfileVisibilityService(
                currentDirectory,
                CreateModuleConfiguration(),
                new LauncherUpdateTargetFactory());

            var visible = service.SelectVisibleProfiles(profiles);

            Assert.Equal(["Default"], visible.Select(static profile => profile.ProfileId).ToArray());
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static IReadOnlyList<LauncherProfileDefinition> CreateProcessProfiles(string currentDirectory)
    {
        var hostExecutable = Path.Combine(currentDirectory, "host", "IIoT.Edge.Shell");
        return
        [
            Profile("Default", "维护模式", hostExecutable),
            Profile("TestPluginLine", "测试插件", hostExecutable),
            Profile("TestPluginAlphaLine", "测试插件甲", hostExecutable),
            Profile("TestPluginBetaLine", "测试插件乙", hostExecutable)
        ];
    }

    private static LauncherProfileDefinition Profile(
        string profileId,
        string displayName,
        string executablePath)
        => new(
            profileId,
            displayName,
            "测试工序",
            null,
            profileId,
            executablePath,
            "Shell",
            "#000000");

    private static IEdgeProfileModuleConfigurationStore CreateModuleConfiguration()
        => new StubModuleConfigurationStore(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = [],
            ["TestPluginLine"] = ["TestPlugin"],
            ["TestPluginAlphaLine"] = ["TestPluginAlpha"],
            ["TestPluginBetaLine"] = ["TestPluginBeta"]
        });

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-launcher-profile-visibility-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

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

    private sealed class StubModuleConfigurationStore(
        IReadOnlyDictionary<string, IReadOnlyList<string>> modulesByMachineProfile)
        : IEdgeProfileModuleConfigurationStore
    {
        public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
            => modulesByMachineProfile.TryGetValue(target.MachineProfile, out var moduleIds)
                ? moduleIds
                : [];

        public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
        {
        }
    }
}
