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
                WriteEnabledSelection(launcherDirectory, "AP", "CP");
                var source = new LauncherPluginActivationSource(launcherDirectory);
                var reconciler = new LauncherPluginActivationReconciler(launcherDirectory, source);
                reconciler.Reconcile();
                var profiles = new LauncherProfileCatalog(
                        launcherDirectory,
                        activationSource: source,
                        activationReconciler: reconciler)
                    .LoadProfiles();

                Assert.Equal(3, profiles.Count);
                var apProfile = Assert.Single(
                    profiles,
                    profile => profile.ProfileId == "DieCuttingAnodeLine");
                var cpProfile = Assert.Single(
                    profiles,
                    profile => profile.ProfileId == "DieCuttingCathodeLine");
                Assert.Equal(["AP"], apProfile.ExpectedModuleIds);
                Assert.Equal(["CP"], cpProfile.ExpectedModuleIds);
                Assert.Equal("AP", apProfile.ActivationModuleId);
                Assert.Equal("AP", apProfile.ActivationPluginDirectory);
                Assert.Equal("CP", cpProfile.ActivationModuleId);
                Assert.Equal("CP", cpProfile.ActivationPluginDirectory);
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
            WriteEnabledSelection(launcherDirectory, "AP", "CP");

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
            WriteEnabledSelection(launcherDirectory, "AP", "CP");

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
            WriteEnabledSelection(launcherDirectory, "AP");
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
                WriteEnabledSelection(launcherDirectory, "AP");
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
                WriteEnabledSelection(launcherDirectory, "AP");
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

    [Fact]
    public void Catalog_WhenEnabledSelectionIsMissing_ShouldFailClosedAndPublishDiagnostic()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            Directory.CreateDirectory(launcherDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            var diagnostics = new LauncherStartupDiagnosticStore();
            var selection = new LauncherEnabledPluginSelectionSource(launcherDirectory, diagnostics);
            var source = new LauncherPluginActivationSource(launcherDirectory, selection, diagnostics);

            var profiles = new LauncherProfileCatalog(
                    launcherDirectory,
                    activationSource: source)
                .LoadProfiles();

            Assert.Equal("Default", Assert.Single(profiles).ProfileId);
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_PLUGIN_SELECTION_MISSING");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Catalog_WhenEnabledSelectionIsCorrupt_ShouldFailClosedWithoutUsingPluginDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            Directory.CreateDirectory(launcherDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            WriteText(
                Path.Combine(
                    EdgeClientProgramDataPaths.ResolveLauncherDirectory(launcherDirectory),
                    LauncherEnabledPluginSelectionSource.EnabledPluginsFileName),
                "{ corrupt");
            var diagnostics = new LauncherStartupDiagnosticStore();
            var selection = new LauncherEnabledPluginSelectionSource(launcherDirectory, diagnostics);
            var source = new LauncherPluginActivationSource(launcherDirectory, selection, diagnostics);

            var profiles = new LauncherProfileCatalog(
                    launcherDirectory,
                    activationSource: source)
                .LoadProfiles();

            Assert.Equal("Default", Assert.Single(profiles).ProfileId);
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_PLUGIN_SELECTION_UNREADABLE"
                        && item.ExceptionType?.Contains("Json", StringComparison.Ordinal) == true);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Selection_WhenSchemaIsMissingInvalidOrUnsupported_ShouldFailClosed()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var selectionPath = Path.Combine(
                EdgeClientProgramDataPaths.ResolveLauncherDirectory(launcherDirectory),
                LauncherEnabledPluginSelectionSource.EnabledPluginsFileName);
            var diagnostics = new LauncherStartupDiagnosticStore();
            var source = new LauncherEnabledPluginSelectionSource(
                launcherDirectory,
                diagnostics);

            foreach (var manifest in new[]
                     {
                         """{"plugins":[{"moduleId":"AP","pluginDirectory":"AP"}]}""",
                         """{"schemaVersion":"1","plugins":[{"moduleId":"AP","pluginDirectory":"AP"}]}""",
                         """{"schemaVersion":2,"plugins":[{"moduleId":"AP","pluginDirectory":"AP"}]}""",
                         """{"schemaVersion":1,"plugins":[{"moduleId":"AP"}]}""",
                         """{"schemaVersion":1,"plugins":[{"moduleId":"AP","pluginDirectory":"../AP"}]}""",
                         """{"schemaVersion":1,"plugins":[{"moduleId":"AP","pluginDirectory":"Shared"},{"moduleId":"CP","pluginDirectory":"Shared"}]}"""
                     })
            {
                WriteText(selectionPath, manifest);

                var selection = source.Load();

                Assert.False(selection.ManifestIsValid);
                Assert.Empty(selection.Plugins);
                Assert.Contains(
                    diagnostics.Snapshot,
                    item => item.ReasonCode == "LAUNCHER_PLUGIN_SELECTION_INVALID");
            }
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Catalog_WhenUnselectedDirectoryImpersonatesModuleId_ShouldFailClosed()
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
                pluginDirectory: "ManualDrop");
            WriteEnabledSelectionEntries(launcherDirectory, ("AP", "AP"));
            var diagnostics = new LauncherStartupDiagnosticStore();
            var selection = new LauncherEnabledPluginSelectionSource(launcherDirectory, diagnostics);
            var source = new LauncherPluginActivationSource(launcherDirectory, selection, diagnostics);

            var profiles = new LauncherProfileCatalog(
                    launcherDirectory,
                    activationSource: source)
                .LoadProfiles();

            Assert.Equal("Default", Assert.Single(profiles).ProfileId);
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_PLUGIN_SELECTED_NOT_DISCOVERED"
                        && item.Subject == "AP");
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Selection_PluginDirectoryIdentity_ShouldFollowPlatformPathCaseRules()
    {
        var selection = new LauncherEnabledPluginSelection(
            true,
            [new LauncherEnabledPluginSelectionItem("AP", "AP")]);

        var matched = selection.TryGetByPluginDirectory("ap", out _);

        Assert.Equal(OperatingSystem.IsWindows(), matched);
    }

    [Fact]
    public void Activation_WhenReferenceEscapesIntoCaseVariantDirectory_ShouldFollowPlatformPathCaseRules()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            Directory.CreateDirectory(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            WriteEnabledSelection(launcherDirectory, "AP");

            var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(launcherDirectory);
            var selectedActivationRoot = Path.Combine(pluginsRoot, "AP", "activation");
            var caseVariantActivationRoot = Path.Combine(pluginsRoot, "ap", "activation");
            var launcherFileName = "launcher.profiles.AP.json";
            var machineFileName = "appsettings.machine.DieCuttingAnodeLine.json";
            WriteText(
                Path.Combine(caseVariantActivationRoot, "launcher", launcherFileName),
                File.ReadAllText(Path.Combine(selectedActivationRoot, "launcher", launcherFileName)));
            WriteText(
                Path.Combine(caseVariantActivationRoot, "machine", machineFileName),
                File.ReadAllText(Path.Combine(selectedActivationRoot, "machine", machineFileName)));
            WriteText(
                Path.Combine(selectedActivationRoot, "manifest.json"),
                $$"""
                {
                  "schemaVersion": 1,
                  "moduleId": "AP",
                  "profiles": [
                    {
                      "profileId": "DieCuttingAnodeLine",
                      "launcherProfile": "../../ap/activation/launcher/{{launcherFileName}}",
                      "machineConfig": "../../ap/activation/machine/{{machineFileName}}"
                    }
                  ]
                }
                """);
            var diagnostics = new LauncherStartupDiagnosticStore();
            var selection = new LauncherEnabledPluginSelectionSource(launcherDirectory, diagnostics);
            var source = new LauncherPluginActivationSource(launcherDirectory, selection, diagnostics);

            var activations = source.LoadActivations();

            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, activations.Count);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Contains(
                    diagnostics.Snapshot,
                    item => item.ReasonCode == "LAUNCHER_PLUGIN_ACTIVATION_INVALID"
                            && item.Subject == "AP");
            }
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Theory]
    [InlineData("MissingDirectory")]
    [InlineData("MissingPluginManifest")]
    [InlineData("MissingActivationManifest")]
    public void Activation_WhenSelectedPluginIsNotDiscoverable_ShouldPublishPerModuleDiagnostic(
        string scenario)
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var pluginDirectory = Path.Combine(
                EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(launcherDirectory),
                "AP");
            Directory.CreateDirectory(Path.GetDirectoryName(pluginDirectory)!);
            if (scenario != "MissingDirectory")
            {
                Directory.CreateDirectory(pluginDirectory);
            }
            if (scenario == "MissingActivationManifest")
            {
                WriteText(
                    Path.Combine(pluginDirectory, "plugin.json"),
                    """{"moduleId":"AP","version":"2.0.17","hostApiVersion":"2.0.0"}""");
            }

            WriteEnabledSelection(launcherDirectory, "AP");
            var diagnostics = new LauncherStartupDiagnosticStore();
            var selection = new LauncherEnabledPluginSelectionSource(launcherDirectory, diagnostics);
            var source = new LauncherPluginActivationSource(launcherDirectory, selection, diagnostics);

            var activations = source.LoadActivations();

            Assert.Empty(activations);
            Assert.Contains(
                diagnostics.Snapshot,
                item => item.ReasonCode == "LAUNCHER_PLUGIN_SELECTED_NOT_DISCOVERED"
                        && item.Subject == "AP"
                        && item.RepairTarget == LauncherStartupDiagnosticRepairTargets.PluginActivation);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void Reconciler_WhenPluginDirectoryIsNotSelected_ShouldNotExposeOrMaterializeIt()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var launcherDirectory = Path.Combine(tempDirectory, "install", "current", "launcher");
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            WriteBaseCatalog(launcherDirectory);
            WriteActivation(launcherDirectory, "AP", "DieCuttingAnodeLine", "负极模切");
            WriteActivation(launcherDirectory, "CP", "DieCuttingCathodeLine", "正极模切");
            WriteEnabledSelection(launcherDirectory, "AP");
            var source = new LauncherPluginActivationSource(launcherDirectory);
            var reconciler = new LauncherPluginActivationReconciler(launcherDirectory, source);

            reconciler.Reconcile();
            var profiles = new LauncherProfileCatalog(
                    launcherDirectory,
                    activationSource: source,
                    activationReconciler: reconciler)
                .LoadProfiles();

            Assert.Contains(profiles, profile => profile.ProfileId == "DieCuttingAnodeLine");
            Assert.DoesNotContain(profiles, profile => profile.ProfileId == "DieCuttingCathodeLine");
            Assert.True(File.Exists(EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                "DieCuttingAnodeLine",
                hostDirectory)));
            Assert.False(File.Exists(EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                "DieCuttingCathodeLine",
                hostDirectory)));
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
        string clientCode = "",
        string? pluginDirectory = null)
    {
        var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(launcherDirectory);
        var pluginRoot = Path.Combine(pluginsRoot, pluginDirectory ?? moduleId);
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

    private static void WriteEnabledSelection(
        string launcherDirectory,
        params string[] moduleIds)
        => WriteEnabledSelectionEntries(
            launcherDirectory,
            moduleIds
                .Select(static moduleId => (ModuleId: moduleId, PluginDirectory: moduleId))
                .ToArray());

    private static void WriteEnabledSelectionEntries(
        string launcherDirectory,
        params (string ModuleId, string PluginDirectory)[] plugins)
        => WriteText(
            Path.Combine(
                EdgeClientProgramDataPaths.ResolveLauncherDirectory(launcherDirectory),
                LauncherEnabledPluginSelectionSource.EnabledPluginsFileName),
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                plugins = plugins
                    .Select(static plugin => new
                    {
                        moduleId = plugin.ModuleId,
                        pluginDirectory = plugin.PluginDirectory
                    })
                    .ToArray()
            }));

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
