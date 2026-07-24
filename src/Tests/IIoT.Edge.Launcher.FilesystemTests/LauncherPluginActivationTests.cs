using System.Text.Json;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherPluginActivationTests
{
    [Fact]
    public void CatalogAndReconciler_WithTwoValidPluginActivations_ShouldExposeTwoProfilesAndSeedMachineConfigs()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            WriteActivation(launcherDirectory, "CP", "DieCuttingCathodeLine", "正极模切");

            WithDataRoot(dataRoot, () =>
            {
                var source = new LauncherPluginActivationSource(launcherDirectory);
                var reconciler = new LauncherPluginActivationReconciler(launcherDirectory, source);
                reconciler.Reconcile();
                var profiles = new LauncherProfileCatalog(
                        launcherDirectory,
                        activationSource: source,
                        activationReconciler: reconciler)
                    .LoadProfiles();

                Assert.Equal(3, profiles.Count);
                Assert.Contains(profiles, profile => profile.ProfileId == "DieCuttingAnodeLine");
                Assert.Contains(profiles, profile => profile.ProfileId == "DieCuttingCathodeLine");
                AssertMachineConfig(hostDirectory, "DieCuttingAnodeLine", "AP");
                AssertMachineConfig(hostDirectory, "DieCuttingCathodeLine", "CP");
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void CatalogAndReconciler_WithVelopackCurrentLayout_ShouldUsePackagedHostForEveryActivation()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempDirectory, "install");
            var launcherDirectory = Path.Combine(installRoot, "current");
            var hostDirectory = Path.Combine(launcherDirectory, "host");
            var shellExecutable = Path.Combine(hostDirectory, "IIoT.Edge.Shell.exe");
            Directory.CreateDirectory(hostDirectory);
            File.WriteAllBytes(shellExecutable, []);
            WriteBaseCatalog(launcherDirectory, "host/IIoT.Edge.Shell");
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            WriteActivation(launcherDirectory, "CP", "DieCuttingCathodeLine", "正极模切");

            var source = new LauncherPluginActivationSource(launcherDirectory);
            var reconciler = new LauncherPluginActivationReconciler(launcherDirectory, source);
            reconciler.Reconcile();
            var profiles = new LauncherProfileCatalog(
                    launcherDirectory,
                    activationSource: source,
                    activationReconciler: reconciler)
                .LoadProfiles();

            Assert.Equal(3, profiles.Count);
            Assert.All(
                profiles,
                profile => Assert.Equal(
                    Path.Combine(hostDirectory, "IIoT.Edge.Shell"),
                    profile.ExecutablePath));
            foreach (var profileId in new[] { "DieCuttingAnodeLine", "DieCuttingCathodeLine" })
            {
                var profile = Assert.Single(profiles, item => item.ProfileId == profileId);
                var launchTarget = ShellLaunchTargetResolver.Resolve(
                    profile.ExecutablePath,
                    isWindows: true,
                    File.Exists);
                Assert.Equal(shellExecutable, launchTarget.FileName);
                Assert.Equal(hostDirectory, launchTarget.WorkingDirectory);
            }

            AssertMachineConfig(hostDirectory, "DieCuttingAnodeLine", "AP");
            AssertMachineConfig(hostDirectory, "DieCuttingCathodeLine", "CP");
            Assert.False(Directory.Exists(Path.Combine(installRoot, "host")));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Catalog_WhenOneActivationContainsCloudIdentity_ShouldSkipOnlyInvalidContribution()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            Directory.CreateDirectory(launcherDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(
                launcherDirectory,
                "AP",
                "DieCuttingAnodeLine",
                "负极模切",
                clientCode: "SHOULD-NOT-BE-PACKAGED");
            WriteActivation(launcherDirectory, "CP", "DieCuttingCathodeLine", "正极模切");

            var profiles = new LauncherProfileCatalog(launcherDirectory).LoadProfiles();

            Assert.Equal(2, profiles.Count);
            Assert.DoesNotContain(profiles, profile => profile.ProfileId == "DieCuttingAnodeLine");
            Assert.Contains(profiles, profile => profile.ProfileId == "DieCuttingCathodeLine");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Catalog_WhenMachineIdentityDoesNotMatchProfile_ShouldSkipInvalidContribution()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            Directory.CreateDirectory(launcherDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            var machinePath = Path.Combine(
                EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(launcherDirectory),
                "AP",
                "activation",
                "machine",
                "appsettings.machine.DieCuttingAnodeLine.json");
            WriteText(
                machinePath,
                File.ReadAllText(machinePath)
                    .Replace(
                        "\"InstanceId\": \"DieCuttingAnodeLine\"",
                        "\"InstanceId\": \"WrongLine\"",
                        StringComparison.Ordinal));

            var profile = Assert.Single(new LauncherProfileCatalog(launcherDirectory).LoadProfiles());

            Assert.Equal("Default", profile.ProfileId);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Reconciler_ShouldPreserveExistingMachineValuesAndFillMissingDefaults()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");

            WithDataRoot(dataRoot, () =>
            {
                var externalPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                    "DieCuttingAnodeLine",
                    hostDirectory);
                WriteText(
                    externalPath,
                    """
                    {
                      "InstanceId": "ExistingInstance",
                      "CloudApi": {
                        "ClientCode": "DEV-KEEP",
                        "BootstrapSecret": "SECRET-KEEP"
                      },
                      "Modules": { "Enabled": [] }
                    }
                    """);

                var source = new LauncherPluginActivationSource(launcherDirectory);
                new LauncherPluginActivationReconciler(launcherDirectory, source).Reconcile();

                using var document = JsonDocument.Parse(File.ReadAllText(externalPath));
                var root = document.RootElement;
                Assert.Equal("ExistingInstance", root.GetProperty("InstanceId").GetString());
                Assert.Equal("DEV-KEEP", root.GetProperty("CloudApi").GetProperty("ClientCode").GetString());
                Assert.Equal("SECRET-KEEP", root.GetProperty("CloudApi").GetProperty("BootstrapSecret").GetString());
                Assert.Equal(
                    "AP",
                    Assert.Single(root.GetProperty("Modules").GetProperty("Enabled").EnumerateArray()).GetString());
                Assert.True(root.TryGetProperty("Shell", out _));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Catalog_WhenExistingMachineConfigCannotBeReconciled_ShouldKeepDefaultFallbackOnly()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");

            WithDataRoot(dataRoot, () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                        "DieCuttingAnodeLine",
                        hostDirectory),
                    "{ invalid existing json");
                var source = new LauncherPluginActivationSource(launcherDirectory);
                var reconciler = new LauncherPluginActivationReconciler(launcherDirectory, source);

                reconciler.Reconcile();
                var profile = Assert.Single(
                    new LauncherProfileCatalog(
                            launcherDirectory,
                            activationSource: source,
                            activationReconciler: reconciler)
                        .LoadProfiles());

                Assert.Equal("Default", profile.ProfileId);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static void AssertMachineConfig(
        string hostDirectory,
        string profileId,
        string moduleId)
    {
        var path = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(profileId, hostDirectory);
        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(profileId, document.RootElement.GetProperty("InstanceId").GetString());
        Assert.Equal(
            profileId,
            document.RootElement.GetProperty("Shell").GetProperty("MachineProfile").GetString());
        Assert.Equal(
            moduleId,
            Assert.Single(
                    document.RootElement.GetProperty("Modules").GetProperty("Enabled").EnumerateArray())
                .GetString());
    }

    private static void WriteBaseCatalog(
        string launcherDirectory,
        string executablePath = "../host/IIoT.Edge.Shell")
        => WriteText(
            Path.Combine(launcherDirectory, "launcher.profiles.json"),
            $$"""
            [
              {
                "ProfileId": "Default",
                "DisplayName": "Edge Host",
                "MachineProfile": "Default",
                "ExecutablePath": "{{executablePath}}"
              }
            ]
            """);

    private static void WriteActivation(
        string launcherDirectory,
        string moduleId,
        string profileId,
        string displayName,
        string clientCode = "")
    {
        var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(launcherDirectory);
        var pluginRoot = Path.Combine(pluginsRoot, moduleId);
        var activationRoot = Path.Combine(pluginRoot, "activation");
        WriteText(
            Path.Combine(pluginRoot, "plugin.json"),
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "version": "2.0.4",
              "hostApiVersion": "2.0.0"
            }
            """);
        WriteText(
            Path.Combine(activationRoot, "manifest.json"),
            $$"""
            {
              "schemaVersion": 1,
              "moduleId": "{{moduleId}}",
              "profiles": [
                {
                  "profileId": "{{profileId}}",
                  "launcherProfile": "launcher/launcher.profiles.{{moduleId}}.json",
                  "machineConfig": "machine/appsettings.machine.{{profileId}}.json"
                }
              ]
            }
            """);
        WriteText(
            Path.Combine(activationRoot, "launcher", $"launcher.profiles.{moduleId}.json"),
            $$"""
            [
              {
                "ProfileId": "{{profileId}}",
                "DisplayName": "{{displayName}}",
                "Description": "{{displayName}} profile",
                "MachineProfile": "{{profileId}}",
                "ExecutablePath": "../host/IIoT.Edge.Shell"
              }
            ]
            """);
        WriteText(
            Path.Combine(activationRoot, "machine", $"appsettings.machine.{profileId}.json"),
            $$"""
            {
              "InstanceId": "{{profileId}}",
              "Shell": {
                "MachineProfile": "{{profileId}}",
                "RuntimeDataRoot": "%ProgramData%/IIoT/EdgeData/Profiles/{{profileId}}"
              },
              "CloudApi": { "ClientCode": "{{clientCode}}", "BootstrapSecret": "" },
              "Modules": {
                "Enabled": [ "{{moduleId}}" ],
                "{{moduleId}}": {}
              }
            }
            """);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "iiot-edge-activation-tests", Guid.NewGuid().ToString("N"));
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
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private static void WithDataRoot(string dataRoot, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
            dataRoot);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                previous);
        }
    }
}
