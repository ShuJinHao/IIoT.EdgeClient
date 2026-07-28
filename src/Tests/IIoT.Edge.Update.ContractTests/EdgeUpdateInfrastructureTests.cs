using System.IO.Compression;
using System.Security.Cryptography;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Infrastructure.Update.Configuration;
using IIoT.Edge.Infrastructure.Update.Host;
using IIoT.Edge.Infrastructure.Update.Packages;
using IIoT.Edge.Infrastructure.Update.Plugins;
using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;
using Xunit;

namespace IIoT.Edge.Update.ContractTests;

public sealed class EdgeUpdateInfrastructureTests
{
    [Fact]
    public void ConfigurationProvider_ShouldReadExternalProfileIdentityAndReleaseOptions()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "CloudApi": {
                    "BaseUrl": "https://packaged-configuration-must-not-win.example.test",
                    "ClientCode": "PACKAGED",
                    "BootstrapSecret": "packaged-secret"
                  }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory),
                    """
                    {
                      "CloudApi": {
                        "BaseUrl": "https://cloud.example.test",
                        "TimeoutSecs": 7,
                        "ClientCode": "EDGE-001",
                        "BootstrapSecret": "secret",
                        "Paths": {
                          "DeviceInstance": "/api/v1/bootstrap/device-instance",
                          "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                          "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                          "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                        }
                      }
                    }
                    """);
                WriteText(
                    EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(hostDirectory),
                    """
                    {
                      "source": "https://updates.example.test/stable/",
                      "channel": "beta",
                      "targetRuntime": "win-arm64"
                    }
                    """);

                WithEnvironmentVariables(
                    new Dictionary<string, string?>
                    {
                        ["CloudApi__BaseUrl"] = "https://environment-must-not-win.example.test",
                        ["CloudApi__ClientCode"] = "ENVIRONMENT",
                        ["CloudApi__BootstrapSecret"] = "environment-secret",
                        ["IIOT_EDGE_UPDATE_URL"] = "https://environment-updates-must-not-win.example.test",
                        ["IIOT_EDGE_RELEASE_CHANNEL"] = "environment-channel",
                        ["IIOT_EDGE_TARGET_RUNTIME"] = "environment-runtime"
                    },
                    () =>
                    {
                        var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory);
                        var target = Target(hostDirectory);

                        var result = provider.Resolve(target);
                        var releaseOptions = provider.ResolveReleaseOptions();

                        Assert.True(result.Success);
                        Assert.Equal("EDGE-001", result.Options!.ClientCode);
                        Assert.Equal("secret", result.Options.BootstrapSecret);
                        Assert.Equal("https://cloud.example.test", result.Options.BaseUrl);
                        Assert.Equal("/api/v1/edge/client-releases/version-reports", result.Options.ClientVersionReportPath);
                        Assert.Equal("/api/v1/edge/runtime-heartbeats", result.Options.RuntimeHeartbeatPath);
                        Assert.Equal("beta", releaseOptions.Channel);
                        Assert.Equal("win-arm64", releaseOptions.TargetRuntime);
                    });
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ConfigurationProvider_ShouldReportIncompleteWithoutBootstrapSecret()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.json"),
                """
                {
                  "CloudApi": {
                    "BaseUrl": "https://cloud.example.test",
                    "BootstrapSecret": "packaged-secret-must-not-win",
                    "Paths": {
                      "DeviceInstance": "/api/v1/bootstrap/device-instance",
                      "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                      "ClientVersionReport": "/api/v1/edge/client-releases/version-reports"
                    }
                  }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory),
                    """
                    {
                      "CloudApi": {
                        "BaseUrl": "https://cloud.example.test",
                        "ClientCode": "DEV-AAAAAAAAAA",
                        "Paths": {
                          "DeviceInstance": "/api/v1/bootstrap/device-instance",
                          "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                          "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                          "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                        }
                      }
                    }
                    """);

                var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory);

                var result = provider.Resolve(Target(hostDirectory));

                Assert.False(result.Success);
                Assert.Contains("BootstrapSecret", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ConfigurationProvider_WhenProfileCloudSwitchIsDisabled_ShouldStillResolveReleaseControlConfiguration()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WithDataRoot(dataRoot, () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                        "LineA",
                        hostDirectory),
                    """
                    {
                      "CloudApi": {
                        "BaseUrl": "https://cloud.example.test",
                        "ClientCode": "EDGE-001",
                        "BootstrapSecret": "secret",
                        "Paths": {
                          "DeviceInstance": "/api/v1/bootstrap/device-instance",
                          "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                          "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                          "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                        }
                      }
                    }
                    """);
                WriteText(
                    EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
                        "LineA",
                        hostDirectory),
                    """{"version":1,"enabled":false}""");

                var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory);
                var result = provider.Resolve(Target(hostDirectory));

                Assert.True(result.Success, result.ErrorMessage);
                Assert.Equal("EDGE-001", result.Options!.ClientCode);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_WhenCloudBusinessSwitchIsDisabled_ShouldStillBootstrapCatalogAndReportVersion()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            await WithDataRootAsync(dataRoot, async () =>
            {
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                        "LineA",
                        hostDirectory),
                    """
                    {
                      "CloudApi": {
                        "BaseUrl": "https://cloud.example.test",
                        "ClientCode": "EDGE-001",
                        "BootstrapSecret": "secret",
                        "Paths": {
                          "DeviceInstance": "/api/v1/bootstrap/device-instance",
                          "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                          "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                          "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                        }
                      },
                      "Modules": {
                        "Enabled": [ "AP" ]
                      }
                    }
                    """);
                WriteText(
                    EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
                        "LineA",
                        hostDirectory),
                    """{"version":1,"enabled":false}""");
                var updateConfigPath =
                    EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(
                        hostDirectory);
                WriteText(
                    updateConfigPath,
                    """
                    {
                      "source": "https://updates.example.test/stable/",
                      "channel": "stable",
                      "targetRuntime": "win-x64"
                    }
                    """);
                var catalog = CatalogWithHostVersions(
                    [HostRelease("2.0.10", EdgeClientHostRuntime.HostApiVersion)],
                    PluginComponent(
                        "AP",
                        Release(
                            "AP",
                            "2.0.11",
                            EdgeClientHostRuntime.HostApiVersion))) with
                {
                    HostUpdateSource = "https://updates.example.test/stable/"
                };
                var sessionClient = new RecordingDeviceSessionClient();
                var catalogClient = new RecordingCatalogClient(catalog);
                var reporter = new RecordingVersionReporter();
                var service = new EdgeReleaseService(
                    new FileEdgeUpdateConfigurationProvider(hostDirectory),
                    sessionClient,
                    catalogClient,
                    reporter,
                    new FixedInstalledPluginCatalog(
                        [InstalledPlugin("AP", "2.0.10", "1.0.0", "99.0.0")]),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new NoopPluginPackageInstaller(),
                    new NoopHostUpdateService(),
                    new EdgeVersionCompatibilityPolicy(),
                    new FileEdgeReleaseSourceValidator(
                        new EdgeUpdateConfigPaths(
                            updateConfigPath,
                            Path.Combine(
                                hostDirectory,
                                FileEdgeUpdateConfigInitializer.SampleConfigFileName))));

                var result = await service.CheckReleaseCatalogAsync(
                    Target(hostDirectory),
                    TestContext.Current.CancellationToken);

                Assert.Equal(EdgeReleaseCatalogState.Succeeded, result.State);
                Assert.Equal(1, sessionClient.BootstrapCallCount);
                Assert.Equal(1, catalogClient.CatalogCallCount);
                Assert.Equal(1, reporter.ReportCallCount);
                var ap = Assert.Single(
                    result.Components,
                    component => component.ModuleId == "AP");
                Assert.Equal("2.0.10", ap.CurrentVersion);
                Assert.Contains(
                    ap.Versions,
                    version => version.Version == "2.0.11"
                               && version.Status == EdgeVersionStatus.Newer
                               && version.CanApply);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void FileProfileCloudSwitchReader_ShouldFailClosedAndReadOnlyTheGeneratedProjection()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WithDataRoot(dataRoot, () =>
            {
                var reader = new FileProfileCloudSwitchReader(hostDirectory);
                var target = Target(hostDirectory);
                Assert.False(reader.IsEnabled(target));

                var projectionPath = EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
                    "LineA",
                    hostDirectory);
                WriteText(projectionPath, """{"version":1,"enabled":true}""");
                Assert.True(reader.IsEnabled(target));

                WriteText(projectionPath, """{"version":1,"enabled":false}""");
                Assert.False(reader.IsEnabled(target));

                WriteText(projectionPath, """{"version":2,"enabled":true}""");
                Assert.False(reader.IsEnabled(target));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ProfileModuleConfiguration_EnableModules_ShouldWriteExternalMachineConfigAndPreserveIdentity()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": {
                    "ClientCode": "EDGE-001",
                    "BootstrapSecret": "secret"
                  },
                  "Modules": {
                    "Enabled": [ "TestPlugin" ]
                  }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var target = Target(hostDirectory);
                var store = new FileEdgeProfileModuleConfigurationStore();

                store.EnableModules(target, ["Welding"]);

                var enabled = store.ReadEnabledModules(target);
                var externalConfig = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory));

                Assert.Equal(["TestPlugin", "Welding"], enabled);
                Assert.Contains("\"ClientCode\": \"EDGE-001\"", externalConfig, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task PluginPackageInstaller_ShouldInstallPackageIntoApplicationPluginsRoot()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.TestPlugin-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "TestPlugin", "1.2.0");
            var release = ReleaseWithPackage(packagePath);
            var installer = new EdgePluginPackageInstaller(new EdgeVersionCompatibilityPolicy());

            await WithDataRootAsync(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Target(hostDirectory),
                    release,
                    CloudOptions(),
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion);

                var pluginDirectory = Path.Combine(
                    EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(hostDirectory),
                    "TestPlugin");
                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(File.Exists(Path.Combine(pluginDirectory, "plugin.json")));
                Assert.True(File.Exists(Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll")));
                Assert.True(File.Exists(Path.Combine(pluginDirectory, "install.json")));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task PluginPackageInstaller_ShouldRejectPackagePathTraversal()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.TestPlugin-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "TestPlugin", "1.2.0", "..\\evil.dll");
            var release = ReleaseWithPackage(packagePath);
            var installer = new EdgePluginPackageInstaller(new EdgeVersionCompatibilityPolicy());

            await WithDataRootAsync(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Target(hostDirectory),
                    release,
                    CloudOptions(),
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion);

                Assert.False(result.Success);
                Assert.Contains("非法路径", result.ErrorMessage, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task PluginPackageInstaller_ShouldRejectActivationThatCarriesCloudIdentityBeforeFormalMutation()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var packagePath = Path.Combine(
                tempDirectory,
                "IIoT.EdgePlugin.TestPlugin-1.2.0-win-x64.zip");
            CreatePluginPackageWithInvalidActivation(
                packagePath,
                "TestPlugin",
                "1.2.0");
            var release = ReleaseWithPackage(packagePath);
            var installer = new EdgePluginPackageInstaller(
                new EdgeVersionCompatibilityPolicy());

            await WithDataRootAsync(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Target(hostDirectory),
                    release,
                    CloudOptions(),
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion);

                Assert.False(result.Success);
                Assert.Contains(
                    "不得携带 Cloud 身份",
                    result.ErrorMessage ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.False(Directory.Exists(Path.Combine(
                    EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(
                        hostDirectory),
                    "TestPlugin")));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task PluginPackageInstaller_ShouldRejectPackageWhenFileCountExceedsLimit()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.TestPlugin-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "TestPlugin", "1.2.0");
            var release = ReleaseWithPackage(packagePath);
            var installer = new EdgePluginPackageInstaller(
                new HttpClient(),
                new EdgeVersionCompatibilityPolicy(),
                EdgePluginPackageInstallLimits.Default with { MaxFileCount = 1 });

            await WithDataRootAsync(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Target(hostDirectory),
                    release,
                    CloudOptions(),
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion);

                Assert.False(result.Success);
                Assert.Contains("文件数量超过限制", result.ErrorMessage, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void UpdateConfigInitializer_ShouldAtomicallyMigrateLegacyKeysAndRejectCatalogRewrite()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-update-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, "launcher.update.sample.json");
            File.WriteAllText(configPath,
                """
                {
                  "Source": "https://cloud.example.test/edge-updates/velopack/stable/",
                  "Channel": "stable",
                  "TargetRuntime": "win-x64",
                  "preserve": "value"
                }
                """);

            var initializer = new FileEdgeUpdateConfigInitializer(new EdgeUpdateConfigPaths(configPath, samplePath));
            initializer.EnsureConfigExists();
            var migrated = File.ReadAllText(configPath);

            var changed = initializer.TrySyncUpdateSource(
                "https://catalog-must-not-rewrite.example.test/");

            Assert.False(changed);
            var content = File.ReadAllText(configPath);
            Assert.Equal(migrated, content);
            Assert.Contains("\"source\"", content, StringComparison.Ordinal);
            Assert.Contains("\"channel\"", content, StringComparison.Ordinal);
            Assert.Contains("\"targetRuntime\"", content, StringComparison.Ordinal);
            Assert.Contains("\"preserve\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Source\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Channel\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\"TargetRuntime\"", content, StringComparison.Ordinal);
            Assert.Single(Directory.EnumerateFiles(tempDirectory));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void UpdateConfigInitializer_WhenJsonRootIsNotObject_ShouldKeepFileAndReturnWithoutThrowing()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(
                tempDirectory,
                "launcher.update.json");
            var samplePath = Path.Combine(
                tempDirectory,
                "launcher.update.sample.json");
            File.WriteAllText(configPath, "[]");
            File.WriteAllText(
                samplePath,
                """
                {
                  "source": "https://updates.example.test/stable/",
                  "channel": "stable",
                  "targetRuntime": "win-x64"
                }
                """);
            var original = File.ReadAllBytes(configPath);
            var initializer = new FileEdgeUpdateConfigInitializer(
                new EdgeUpdateConfigPaths(configPath, samplePath));

            var exception = Record.Exception(initializer.EnsureConfigExists);

            Assert.Null(exception);
            Assert.Equal(original, File.ReadAllBytes(configPath));
            var validator = new FileEdgeReleaseSourceValidator(
                new EdgeUpdateConfigPaths(configPath, samplePath));
            Assert.Contains(
                "根节点必须是对象",
                validator.ValidateConfiguredSource() ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ReleaseSourceValidator_ShouldRejectCatalogMismatchWithoutChangingFormalConfig()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempDirectory, "launcher.update.json");
            WriteText(
                configPath,
                """
                {
                  "source": "https://updates.example.test/stable/",
                  "channel": "stable",
                  "targetRuntime": "win-x64"
                }
                """);
            var original = File.ReadAllBytes(configPath);
            var validator = new FileEdgeReleaseSourceValidator(
                new EdgeUpdateConfigPaths(
                    configPath,
                    Path.Combine(tempDirectory, "launcher.update.sample.json")));

            var matching = validator.ValidateCatalogSource(
                "https://updates.example.test/stable");
            var matchingHostCase = validator.ValidateCatalogSource(
                "https://UPDATES.EXAMPLE.TEST/stable");
            var pathCaseMismatch = validator.ValidateCatalogSource(
                "https://updates.example.test/Stable");
            var mismatch = validator.ValidateCatalogSource(
                "https://other-updates.example.test/stable/");

            Assert.Null(matching);
            Assert.Null(matchingHostCase);
            Assert.Contains(
                "不一致",
                pathCaseMismatch ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Contains(
                "不一致",
                mismatch ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(configPath));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ReleaseSourceValidator_WhenFileUriDecodesToInvalidPath_ShouldReturnUnavailable()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(
                tempDirectory,
                "launcher.update.json");
            WriteText(
                configPath,
                """
                {
                  "source": "file:///updates/catalog%00invalid",
                  "channel": "stable",
                  "targetRuntime": "win-x64"
                }
                """);
            var validator = new FileEdgeReleaseSourceValidator(
                new EdgeUpdateConfigPaths(
                    configPath,
                    Path.Combine(
                        tempDirectory,
                        "launcher.update.sample.json")));

            var exception = Record.Exception(
                () => validator.ValidateConfiguredSource());
            var result = validator.ValidateConfiguredSource();

            Assert.Null(exception);
            Assert.Contains(
                "更新源无效",
                result ?? string.Empty,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_WhenLauncherUpdateConfigIsMissing_ShouldFailBeforeNetwork()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var service = new EdgeReleaseService(
                new SuccessfulCloudConfigurationProvider(),
                new NotCalledDeviceSessionClient(),
                new NotCalledCatalogClient(),
                new NoopVersionReporter(),
                new FixedInstalledPluginCatalog(
                    [InstalledPlugin("TestPlugin", "2.0.10", "1.0.0", "99.0.0")]),
                new FixedProfileModuleConfigurationStore(["TestPlugin"]),
                new NoopPluginPackageInstaller(),
                new NoopHostUpdateService(),
                new EdgeVersionCompatibilityPolicy(),
                new FileEdgeReleaseSourceValidator(
                    new EdgeUpdateConfigPaths(
                        Path.Combine(tempDirectory, "launcher.update.json"),
                        Path.Combine(tempDirectory, "launcher.update.sample.json"))));

            var result = await service.CheckReleaseCatalogAsync(
                Target(tempDirectory),
                TestContext.Current.CancellationToken);

            Assert.Equal(EdgeReleaseCatalogState.CatalogUnavailable, result.State);
            Assert.Contains(
                "配置不存在",
                result.ErrorMessage ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Equal(
                "2.0.10",
                Assert.Single(
                    result.Components,
                    static component => component.ModuleId == "TestPlugin").CurrentVersion);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_WhenCatalogSourceDiffers_ShouldRejectCatalogWithoutRewritingConfig()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempDirectory, "launcher.update.json");
            WriteText(
                configPath,
                """
                {
                  "source": "https://updates.example.test/stable/",
                  "channel": "stable",
                  "targetRuntime": "win-x64"
                }
                """);
            var original = File.ReadAllBytes(configPath);
            var catalog = Catalog(
                PluginComponent(
                    "TestPlugin",
                    Release(
                        "TestPlugin",
                        "2.0.11",
                        EdgeClientHostRuntime.HostApiVersion))) with
            {
                HostUpdateSource = "https://other-updates.example.test/stable/"
            };
            var service = new EdgeReleaseService(
                new SuccessfulCloudConfigurationProvider(),
                new SuccessfulDeviceSessionClient(),
                new FixedCatalogClient(catalog),
                new NoopVersionReporter(),
                new FixedInstalledPluginCatalog(
                    [InstalledPlugin("TestPlugin", "2.0.10", "1.0.0", "99.0.0")]),
                new FixedProfileModuleConfigurationStore(["TestPlugin"]),
                new NoopPluginPackageInstaller(),
                new NoopHostUpdateService(),
                new EdgeVersionCompatibilityPolicy(),
                new FileEdgeReleaseSourceValidator(
                    new EdgeUpdateConfigPaths(
                        configPath,
                        Path.Combine(tempDirectory, "launcher.update.sample.json"))));

            var result = await service.CheckReleaseCatalogAsync(
                Target(tempDirectory),
                TestContext.Current.CancellationToken);

            Assert.Equal(EdgeReleaseCatalogState.CatalogUnavailable, result.State);
            Assert.Contains("不一致", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllBytes(configPath));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Theory]
    [InlineData("catalog missing")]
    [InlineData("Cloud 请求失败: HTTP 401")]
    [InlineData("Cloud 请求超时")]
    [InlineData("Cloud 请求失败: HTTP 500")]
    [InlineData("invalid JSON")]
    public async Task CheckReleaseCatalogAsync_WhenCatalogCannotBeRead_ShouldKeepLocalFactsAndReturnUnavailable(
        string catalogFailure)
    {
        var installed = InstalledPlugin(
            "TestPlugin",
            "2.0.10",
            "1.0.0",
            "99.0.0");
        var service = new EdgeReleaseService(
            new SuccessfulCloudConfigurationProvider(),
            new SuccessfulDeviceSessionClient(),
            new FailedCatalogClient(catalogFailure),
            new NoopVersionReporter(),
            new FixedInstalledPluginCatalog([installed]),
            new FixedProfileModuleConfigurationStore(["TestPlugin"]),
            new NoopPluginPackageInstaller(),
            new NoopHostUpdateService(),
            new EdgeVersionCompatibilityPolicy());

        var result = await service.CheckReleaseCatalogAsync(
            Target(Path.GetTempPath()),
            TestContext.Current.CancellationToken);

        Assert.Equal(EdgeReleaseCatalogState.CatalogUnavailable, result.State);
        var plugin = Assert.Single(
            result.Components,
            component => component.ComponentKind == EdgeComponentKind.Plugin);
        Assert.Equal("2.0.10", plugin.CurrentVersion);
        Assert.Empty(plugin.Versions);
        Assert.Contains(catalogFailure, result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_WhenCatalogContainsInvalidVersion_ShouldKeepLocalFactsAndReturnUnavailable()
    {
        var installed = InstalledPlugin(
            "TestPlugin",
            "2.0.10",
            "1.0.0",
            "99.0.0");
        var invalidCatalog = Catalog(
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    "not-a-version",
                    EdgeClientHostRuntime.HostApiVersion)));
        var service = new EdgeReleaseService(
            new SuccessfulCloudConfigurationProvider(),
            new SuccessfulDeviceSessionClient(),
            new FixedCatalogClient(invalidCatalog),
            new NoopVersionReporter(),
            new FixedInstalledPluginCatalog([installed]),
            new FixedProfileModuleConfigurationStore(["TestPlugin"]),
            new NoopPluginPackageInstaller(),
            new NoopHostUpdateService(),
            new EdgeVersionCompatibilityPolicy());

        var result = await service.CheckReleaseCatalogAsync(
            Target(Path.GetTempPath()),
            TestContext.Current.CancellationToken);

        Assert.Equal(EdgeReleaseCatalogState.CatalogUnavailable, result.State);
        var plugin = Assert.Single(
            result.Components,
            component => component.ComponentKind == EdgeComponentKind.Plugin);
        Assert.Equal("2.0.10", plugin.CurrentVersion);
        Assert.Empty(plugin.Versions);
        Assert.Contains("非法插件版本", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildVersionPlans_ShouldMarkUpdateAvailableAndIncompatible()
    {
        var catalog = Catalog(
            PluginComponent("TestPlugin", Release("TestPlugin", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("Welding", Release("Welding", "1.0.0", "999.0.0")));
        var installed = new[]
        {
            new EdgeInstalledPlugin(
                "TestPlugin",
                "TestPlugin",
                "测试插件",
                "1.0.0",
                EdgeClientHostRuntime.HostApiVersion,
                "1.0.0",
                "99.0.0",
                [],
                "plugin.json",
                "current")
        };

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            installed,
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy());

        var testplugin = plans.Single(x => x.ModuleId == "TestPlugin");
        var welding = plans.Single(x => x.ModuleId == "Welding");
        Assert.Equal(EdgeVersionStatus.Newer, testplugin.Versions.Single().Status);
        Assert.Equal(EdgeVersionStatus.Incompatible, welding.Versions.Single().Status);
    }

    [Fact]
    public void BuildVersionPlans_ShouldOnlyShowEnabledProfilePlugins()
    {
        var catalog = Catalog(
            PluginComponent("TestPlugin", Release("TestPlugin", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("TestPluginAlpha", Release("TestPluginAlpha", "1.0.0", EdgeClientHostRuntime.HostApiVersion)));

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            [],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy(),
            ["TestPlugin"]);

        Assert.Contains(plans, component => component.ComponentKind == EdgeComponentKind.Host);
        var plugin = Assert.Single(plans, component => component.ComponentKind == EdgeComponentKind.Plugin);
        Assert.Equal("TestPlugin", plugin.ModuleId);
    }

    [Fact]
    public void BuildVersionPlans_WhenEnabledModulesEmpty_ShouldNotExposeCloudPlugins()
    {
        var catalog = Catalog(
            PluginComponent("TestPlugin", Release("TestPlugin", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("TestPluginAlpha", Release("TestPluginAlpha", "1.0.0", EdgeClientHostRuntime.HostApiVersion)));

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            [],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy(),
            []);

        Assert.Single(plans);
        Assert.Equal(EdgeComponentKind.Host, plans[0].ComponentKind);
    }

    [Fact]
    public void BuildVersionPlans_WhenTargetHostNeedsNewPlugin_ShouldRequireCompleteComposition()
    {
        var catalog = CatalogWithHostVersions(
            [
                HostRelease("2.0.0", EdgeClientHostRuntime.HostApiVersion),
                HostRelease("1.0.0", EdgeClientHostRuntime.HostApiVersion)
            ],
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    "2.0.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "2.0.0",
                    "2.9.9"),
                Release(
                    "TestPlugin",
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "1.0.0",
                    "1.9.9")));
        var installed = new[]
        {
            InstalledPlugin("TestPlugin", "1.0.0", "1.0.0", "1.9.9")
        };

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            installed,
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy(),
            ["TestPlugin"]);

        var host = plans.Single(plan => plan.ComponentKind == EdgeComponentKind.Host);
        var target = host.Versions.Single(option => option.Version == "2.0.0");
        Assert.True(target.CanApply);
        Assert.NotNull(target.RequiredComposition);
        Assert.Equal("2.0.0", target.RequiredComposition!.HostVersion);
        Assert.Equal("2.0.0", target.RequiredComposition.PluginVersions["TestPlugin"]);
        Assert.Contains("TestPlugin 2.0.0", target.CompatibilityIssue, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildVersionPlans_WhenTargetHostHasNoCompatiblePlugin_ShouldBlockBeforeInstall()
    {
        var catalog = CatalogWithHostVersions(
            [HostRelease("2.0.0", EdgeClientHostRuntime.HostApiVersion)],
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "1.0.0",
                    "1.9.9")));

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            [InstalledPlugin("TestPlugin", "1.0.0", "1.0.0", "1.9.9")],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy(),
            ["TestPlugin"]);

        var target = plans
            .Single(plan => plan.ComponentKind == EdgeComponentKind.Host)
            .Versions.Single();
        Assert.False(target.CanApply);
        Assert.Null(target.RequiredComposition);
        Assert.Contains("没有兼容宿主 2.0.0", target.CompatibilityIssue, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildVersionPlans_WhenEnabledPluginIsNotInstalled_ShouldRequireItsCompatibleVersion()
    {
        var catalog = CatalogWithHostVersions(
            [HostRelease("2.0.0", EdgeClientHostRuntime.HostApiVersion)],
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    "2.0.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "2.0.0",
                    "2.9.9")));

        var plans = EdgeReleaseService.BuildVersionPlans(
            catalog,
            [],
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion,
            new EdgeVersionCompatibilityPolicy(),
            ["TestPlugin"]);

        var target = plans
            .Single(plan => plan.ComponentKind == EdgeComponentKind.Host)
            .Versions.Single();
        Assert.True(target.CanApply);
        Assert.NotNull(target.RequiredComposition);
        Assert.Equal("2.0.0", target.RequiredComposition!.PluginVersions["TestPlugin"]);
    }

    [Fact]
    public async Task ApplyVersionCompositionAsync_WhenMultipleProfilesShareHost_ShouldInstallPluginOnceAndApplyHostOnce()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            const string targetVersion = "99.0.0";
            var catalog = CatalogWithHostVersions(
                [HostRelease(targetVersion, EdgeClientHostRuntime.HostApiVersion)],
                PluginComponent(
                    "TestPlugin",
                    Release(
                        "TestPlugin",
                        targetVersion,
                        EdgeClientHostRuntime.HostApiVersion,
                        targetVersion,
                        targetVersion)));
            var packageInstaller = new RecordingPluginPackageInstaller();
            var hostUpdateService = new RecordingHostUpdateService();
            var service = new EdgeReleaseService(
                new SuccessfulCloudConfigurationProvider(),
                new SuccessfulDeviceSessionClient(),
                new FixedCatalogClient(catalog),
                new NoopVersionReporter(),
                new FileInstalledPluginCatalog(),
                new FixedProfileModuleConfigurationStore(["TestPlugin"]),
                packageInstaller,
                hostUpdateService,
                new EdgeVersionCompatibilityPolicy());
            var firstTarget = Target(tempDirectory);
            var secondTarget = firstTarget with { MachineProfile = "LineB" };

            var result = await service.ApplyVersionCompositionAsync(
                [firstTarget, secondTarget],
                new EdgeVersionSelection(
                    targetVersion,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["TestPlugin"] = targetVersion
                    }),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, packageInstaller.InstallCallCount);
            Assert.Equal(1, hostUpdateService.ApplyCallCount);
            Assert.Equal(targetVersion, hostUpdateService.AppliedVersion);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyVersionCompositionAsync_WhenCatalogsOwnDifferentRelativePackages_ShouldKeepEachReleaseSource()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            const string targetVersion = "99.0.0";
            var hostRelease = HostRelease(
                targetVersion,
                EdgeClientHostRuntime.HostApiVersion);
            var firstCatalog = CatalogWithHostVersions(
                [hostRelease],
                PluginComponent(
                    "FirstPlugin",
                    Release(
                        "FirstPlugin",
                        targetVersion,
                        EdgeClientHostRuntime.HostApiVersion,
                        targetVersion,
                        targetVersion) with
                    {
                        DownloadUrl = "packages/first.zip"
                    }));
            var secondCatalog = CatalogWithHostVersions(
                [hostRelease],
                PluginComponent(
                    "SecondPlugin",
                    Release(
                        "SecondPlugin",
                        targetVersion,
                        EdgeClientHostRuntime.HostApiVersion,
                        targetVersion,
                        targetVersion) with
                    {
                        DownloadUrl = "packages/second.zip"
                    }));
            var firstOptions = CloudOptions(
                "https://line-a.example.test",
                "EDGE-A");
            var secondOptions = CloudOptions(
                "https://line-b.example.test",
                "EDGE-B");
            var transaction = new RecordingCompositionTransaction();
            var hostUpdateService = new RecordingHostUpdateService();
            var service = new EdgeReleaseService(
                new ProfileCloudConfigurationProvider(
                    new Dictionary<string, EdgeUpdateCloudApiOptions>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineA"] = firstOptions,
                        ["LineB"] = secondOptions
                    }),
                new SuccessfulDeviceSessionClient(),
                new ProfileCatalogClient(
                    new Dictionary<string, EdgeReleaseCatalog>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        [firstOptions.BaseUrl] = firstCatalog,
                        [secondOptions.BaseUrl] = secondCatalog
                    }),
                new NoopVersionReporter(),
                new FixedInstalledPluginCatalog([]),
                new ProfileModuleConfigurationStore(
                    new Dictionary<string, IReadOnlyList<string>>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineA"] = ["FirstPlugin"],
                        ["LineB"] = ["SecondPlugin"]
                    }),
                new NoopPluginPackageInstaller(),
                hostUpdateService,
                new EdgeVersionCompatibilityPolicy(),
                releaseSourceValidator: null,
                compositionTransaction: transaction);
            var firstTarget = Target(tempDirectory);
            var secondTarget = firstTarget with { MachineProfile = "LineB" };

            var result = await service.ApplyVersionCompositionAsync(
                [firstTarget, secondTarget],
                new EdgeVersionSelection(
                    targetVersion,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["FirstPlugin"] = targetVersion,
                        ["SecondPlugin"] = targetVersion
                    }),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, transaction.InstallCallCount);
            var firstSource = Assert.Single(
                transaction.Releases,
                item => item.Release.ModuleId == "FirstPlugin");
            Assert.Equal("packages/first.zip", firstSource.Release.DownloadUrl);
            Assert.Equal(firstOptions.BaseUrl, firstSource.CloudOptions.BaseUrl);
            var secondSource = Assert.Single(
                transaction.Releases,
                item => item.Release.ModuleId == "SecondPlugin");
            Assert.Equal("packages/second.zip", secondSource.Release.DownloadUrl);
            Assert.Equal(secondOptions.BaseUrl, secondSource.CloudOptions.BaseUrl);
            Assert.Equal(1, hostUpdateService.ApplyCallCount);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ApplyVersionCompositionAsync_WhenEnabledProfileDoesNotAdvertiseSelectedPlugin_ShouldRejectCrossProfileSource()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            const string targetVersion = "99.0.0";
            var hostRelease = HostRelease(
                targetVersion,
                EdgeClientHostRuntime.HostApiVersion);
            var firstCatalog = CatalogWithHostVersions(
                [hostRelease],
                PluginComponent(
                    "TestPlugin",
                    Release(
                        "TestPlugin",
                        "1.0.0",
                        EdgeClientHostRuntime.HostApiVersion,
                        "1.0.0",
                        targetVersion)));
            var secondCatalog = CatalogWithHostVersions(
                [hostRelease],
                PluginComponent(
                    "TestPlugin",
                    Release(
                        "TestPlugin",
                        targetVersion,
                        EdgeClientHostRuntime.HostApiVersion,
                        targetVersion,
                        targetVersion) with
                    {
                        DownloadUrl = "packages/test-plugin.zip"
                    }));
            var firstOptions = CloudOptions(
                "https://line-a.example.test",
                "EDGE-A");
            var secondOptions = CloudOptions(
                "https://line-b.example.test",
                "EDGE-B");
            var transaction = new RecordingCompositionTransaction();
            var hostUpdateService = new RecordingHostUpdateService();
            var service = new EdgeReleaseService(
                new ProfileCloudConfigurationProvider(
                    new Dictionary<string, EdgeUpdateCloudApiOptions>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineA"] = firstOptions,
                        ["LineB"] = secondOptions
                    }),
                new SuccessfulDeviceSessionClient(),
                new ProfileCatalogClient(
                    new Dictionary<string, EdgeReleaseCatalog>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        [firstOptions.BaseUrl] = firstCatalog,
                        [secondOptions.BaseUrl] = secondCatalog
                    }),
                new NoopVersionReporter(),
                new FixedInstalledPluginCatalog(
                    [InstalledPlugin("TestPlugin", "1.0.0", "1.0.0", targetVersion)]),
                new ProfileModuleConfigurationStore(
                    new Dictionary<string, IReadOnlyList<string>>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        ["LineA"] = ["TestPlugin"],
                        ["LineB"] = []
                    }),
                new NoopPluginPackageInstaller(),
                hostUpdateService,
                new EdgeVersionCompatibilityPolicy(),
                releaseSourceValidator: null,
                compositionTransaction: transaction);
            var firstTarget = Target(tempDirectory);
            var secondTarget = firstTarget with { MachineProfile = "LineB" };

            var result = await service.ApplyVersionCompositionAsync(
                [firstTarget, secondTarget],
                new EdgeVersionSelection(
                    targetVersion,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["TestPlugin"] = targetVersion
                    }),
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains(
                "拒绝跨用其他工序的下载源",
                result.ErrorMessage ?? string.Empty,
                StringComparison.Ordinal);
            Assert.Equal(0, transaction.InstallCallCount);
            Assert.Equal(0, hostUpdateService.ApplyCallCount);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ApplyPluginVersionAsync_WhenPluginHasDependency_ShouldSubmitOneAtomicTransaction()
    {
        var dependency = Release(
            "Dependency",
            "2.0.11",
            EdgeClientHostRuntime.HostApiVersion);
        var plugin = Release(
            "TestPlugin",
            "2.0.11",
            EdgeClientHostRuntime.HostApiVersion) with
        {
            Dependencies = ["Dependency"]
        };
        var catalog = Catalog(
            PluginComponent("Dependency", dependency),
            PluginComponent("TestPlugin", plugin));
        var transaction = new RecordingCompositionTransaction();
        var service = new EdgeReleaseService(
            new SuccessfulCloudConfigurationProvider(),
            new SuccessfulDeviceSessionClient(),
            new FixedCatalogClient(catalog),
            new NoopVersionReporter(),
            new FixedInstalledPluginCatalog([]),
            new FixedProfileModuleConfigurationStore(["TestPlugin"]),
            new NoopPluginPackageInstaller(),
            new NoopHostUpdateService(),
            new EdgeVersionCompatibilityPolicy(),
            releaseSourceValidator: null,
            compositionTransaction: transaction);

        var result = await service.ApplyPluginVersionAsync(
            Target(Path.GetTempPath()),
            "TestPlugin",
            "2.0.11",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, transaction.InstallCallCount);
        Assert.Equal(
            ["Dependency", "TestPlugin"],
            transaction.Releases.Select(static release => release.Release.ModuleId));
        Assert.Null(transaction.PendingHostVersion);
    }

    [Fact]
    public async Task ApplyVersionCompositionAsync_WhenHostHandoffFails_ShouldRollbackPendingPluginTransaction()
    {
        const string targetVersion = "99.0.0";
        var catalog = CatalogWithHostVersions(
            [HostRelease(targetVersion, EdgeClientHostRuntime.HostApiVersion)],
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    targetVersion,
                    EdgeClientHostRuntime.HostApiVersion,
                    targetVersion,
                    targetVersion)));
        var transaction = new RecordingCompositionTransaction();
        var service = new EdgeReleaseService(
            new SuccessfulCloudConfigurationProvider(),
            new SuccessfulDeviceSessionClient(),
            new FixedCatalogClient(catalog),
            new NoopVersionReporter(),
            new FixedInstalledPluginCatalog([]),
            new FixedProfileModuleConfigurationStore(["TestPlugin"]),
            new NoopPluginPackageInstaller(),
            new FailingHostUpdateService(),
            new EdgeVersionCompatibilityPolicy(),
            releaseSourceValidator: null,
            compositionTransaction: transaction);

        var result = await service.ApplyVersionCompositionAsync(
            Target(Path.GetTempPath()),
            new EdgeVersionSelection(
                targetVersion,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TestPlugin"] = targetVersion
                }),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(1, transaction.InstallCallCount);
        Assert.Equal(targetVersion, transaction.PendingHostVersion);
        Assert.Equal(1, transaction.RollbackCallCount);
    }

    [Fact]
    public async Task ApplyVersionCompositionAsync_WhenHostHandoffIsCanceledAndRollbackFails_ShouldReturnFailure()
    {
        const string targetVersion = "99.0.0";
        var catalog = CatalogWithHostVersions(
            [HostRelease(targetVersion, EdgeClientHostRuntime.HostApiVersion)],
            PluginComponent(
                "TestPlugin",
                Release(
                    "TestPlugin",
                    targetVersion,
                    EdgeClientHostRuntime.HostApiVersion,
                    targetVersion,
                    targetVersion)));
        var transaction = new RecordingCompositionTransaction(
            EdgePluginInstallResult.Failed("rollback evidence retained"));
        using var cancellation = new CancellationTokenSource();
        var service = new EdgeReleaseService(
            new SuccessfulCloudConfigurationProvider(),
            new SuccessfulDeviceSessionClient(),
            new FixedCatalogClient(catalog),
            new NoopVersionReporter(),
            new FixedInstalledPluginCatalog([]),
            new FixedProfileModuleConfigurationStore(["TestPlugin"]),
            new NoopPluginPackageInstaller(),
            new CancelingHostUpdateService(cancellation),
            new EdgeVersionCompatibilityPolicy(),
            releaseSourceValidator: null,
            compositionTransaction: transaction);

        var result = await service.ApplyVersionCompositionAsync(
            Target(Path.GetTempPath()),
            new EdgeVersionSelection(
                targetVersion,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TestPlugin"] = targetVersion
                }),
            cancellationToken: cancellation.Token);

        Assert.False(result.Success);
        Assert.Contains("回滚失败", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(1, transaction.RollbackCallCount);
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_ShouldFilterCatalogToCurrentProfileEnabledModules()
    {
        var tempDirectory = CreateTempDirectory();
        var catalog = Catalog(
            PluginComponent("TestPlugin", Release("TestPlugin", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("TestPluginAlpha", Release("TestPluginAlpha", "1.0.0", EdgeClientHostRuntime.HostApiVersion)));
        try
        {
            var service = new EdgeReleaseService(
                new SuccessfulCloudConfigurationProvider(),
                new SuccessfulDeviceSessionClient(),
                new FixedCatalogClient(catalog),
                new NoopVersionReporter(),
                new FileInstalledPluginCatalog(),
                new FixedProfileModuleConfigurationStore(["TestPlugin"]),
                new NoopPluginPackageInstaller(),
                new NoopHostUpdateService(),
                new EdgeVersionCompatibilityPolicy());

            var result = await service.CheckReleaseCatalogAsync(
                Target(tempDirectory),
                TestContext.Current.CancellationToken);

            Assert.Equal(EdgeReleaseCatalogState.Succeeded, result.State);
            Assert.Contains(result.Components, component => component.ComponentKind == EdgeComponentKind.Host);
            var plugin = Assert.Single(
                result.Components,
                component => component.ComponentKind == EdgeComponentKind.Plugin);
            Assert.Equal("TestPlugin", plugin.ModuleId);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task ApplyPluginVersionAsync_WhenModuleOutsideCurrentProfile_ShouldReturnOperatorFacingFailure()
    {
        var tempDirectory = CreateTempDirectory();
        var catalog = Catalog(
            PluginComponent("TestPlugin", Release("TestPlugin", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("TestPluginAlpha", Release("TestPluginAlpha", "1.0.0", EdgeClientHostRuntime.HostApiVersion)));
        try
        {
            var installer = new RecordingPluginPackageInstaller();
            var service = new EdgeReleaseService(
                new SuccessfulCloudConfigurationProvider(),
                new SuccessfulDeviceSessionClient(),
                new FixedCatalogClient(catalog),
                new NoopVersionReporter(),
                new FileInstalledPluginCatalog(),
                new FixedProfileModuleConfigurationStore(["TestPlugin"]),
                installer,
                new NoopHostUpdateService(),
                new EdgeVersionCompatibilityPolicy());

            var result = await service.ApplyPluginVersionAsync(
                Target(tempDirectory),
                "TestPluginAlpha",
                "1.0.0",
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.Contains("不属于当前工序", result.ErrorMessage, StringComparison.Ordinal);
            Assert.Equal(0, installer.InstallCallCount);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public async Task CheckReleaseCatalogAsync_WhenCloudConfigMissing_ShouldStillReturnLocalPlugins()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            await WithDataRootAsync(dataRoot, async () =>
            {
                WriteText(
                    Path.Combine(
                        EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(hostDirectory),
                        "TestPlugin",
                        "plugin.json"),
                    $$"""
                    {
                      "moduleId": "TestPlugin",
                      "displayName": "测试插件",
                      "version": "1.0.0",
                      "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
                      "minHostVersion": "1.0.0",
                      "maxHostVersion": "99.0.0",
                      "entryAssembly": "IIoT.Edge.TestPlugin.dll",
                      "entryType": "IIoT.Edge.TestPlugin.DependencyInjection",
                      "supportedProcessType": "TestPlugin",
                      "dependencies": []
                    }
                    """);
                var service = new EdgeReleaseService(
                    new MissingCloudConfigurationProvider(),
                    new NotCalledDeviceSessionClient(),
                    new NotCalledCatalogClient(),
                    new NoopVersionReporter(),
                    new FileInstalledPluginCatalog(),
                    new EmptyProfileModuleConfigurationStore(),
                    new NoopPluginPackageInstaller(),
                    new NoopHostUpdateService(),
                    new EdgeVersionCompatibilityPolicy());

                var result = await service.CheckReleaseCatalogAsync(Target(hostDirectory));

                Assert.Equal(EdgeReleaseCatalogState.NotConfigured, result.State);
                Assert.Contains(result.Components, component => component.ComponentKind == EdgeComponentKind.Host);
                var plugin = Assert.Single(
                    result.Components,
                    component => component.ComponentKind == EdgeComponentKind.Plugin
                                 && component.ModuleId == "TestPlugin");
                Assert.Equal("测试插件", plugin.DisplayName);
                Assert.Equal("1.0.0", plugin.CurrentVersion);
                Assert.Empty(plugin.Versions);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void HostUpdateService_WhenSourceIsLocalDirectory_ShouldResolveLocalDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var localDirectory = VelopackHostUpdateService.TryResolveLocalDirectory(tempDirectory);

            Assert.NotNull(localDirectory);
            Assert.Equal(Path.GetFullPath(tempDirectory), localDirectory.FullName);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void HostUpdateService_WhenSourceIsWebUrl_ShouldNotResolveLocalDirectory()
    {
        var localDirectory = VelopackHostUpdateService.TryResolveLocalDirectory("https://updates.example/edge/");

        Assert.Null(localDirectory);
    }

    private static EdgeUpdateTarget Target(string hostDirectory)
        => new("LineA", hostDirectory, Path.Combine(hostDirectory, "IIoT.Edge.Shell"));

    private static EdgeUpdateCloudApiOptions CloudOptions()
        => CloudOptions("https://cloud.example.test", "EDGE-001");

    private static EdgeUpdateCloudApiOptions CloudOptions(
        string baseUrl,
        string clientCode)
        => new(
            baseUrl,
            5,
            clientCode,
            "secret",
            "/api/v1/bootstrap/device-instance",
            "/api/v1/edge/client-releases/device/{deviceId}/catalog",
            "/api/v1/edge/client-releases/version-reports",
            "/api/v1/edge/runtime-heartbeats");

    private static EdgeReleaseCatalog Catalog(params EdgePluginReleaseComponent[] plugins)
        => CatalogWithHostVersions(
            [HostRelease("1.0.0", EdgeClientHostRuntime.HostApiVersion)],
            plugins);

    private static EdgeReleaseCatalog CatalogWithHostVersions(
        IReadOnlyList<EdgeHostVersionEntry> hostVersions,
        params EdgePluginReleaseComponent[] plugins)
        => new(
            2,
            "stable",
            "win-x64",
            new EdgeHostReleaseComponent(
                "Host",
                "Edge Host",
                hostVersions),
            plugins,
            DateTime.UtcNow);

    private static EdgeHostVersionEntry HostRelease(string version, string hostApiVersion)
        => new(
            Guid.NewGuid(),
            "stable",
            version,
            hostApiVersion,
            "win-x64",
            "net10.0",
            $"https://cloud.example.test/host-{version}.nupkg",
            new string('A', 64),
            1,
            null,
            "Published",
            null,
            "IIoT",
            DateTime.UtcNow,
            DateTime.UtcNow);

    private static EdgePluginReleaseComponent PluginComponent(
        string moduleId,
        params EdgePluginVersionEntry[] releases)
        => new(
            "Plugin",
            moduleId,
            moduleId,
            null,
            null,
            null,
            releases);

    private static EdgePluginVersionEntry Release(string moduleId, string version, string hostApiVersion)
        => Release(moduleId, version, hostApiVersion, "1.0.0", "99.0.0");

    private static EdgePluginVersionEntry Release(
        string moduleId,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion)
        => new(
            Guid.NewGuid(),
            "stable",
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            "win-x64",
            "net10.0",
            $"https://cloud.example.test/{moduleId}.zip",
            new string('A', 64),
            1,
            null,
            [],
            "Published",
            null,
            "IIoT",
            DateTime.UtcNow,
            DateTime.UtcNow);

    private static EdgeInstalledPlugin InstalledPlugin(
        string moduleId,
        string version,
        string minHostVersion,
        string maxHostVersion)
        => new(
            moduleId,
            moduleId,
            moduleId,
            version,
            EdgeClientHostRuntime.HostApiVersion,
            minHostVersion,
            maxHostVersion,
            [],
            "plugin.json",
            "current");

    private static EdgePluginVersionRelease ReleaseWithPackage(string packagePath)
    {
        CreateShaIfMissing(packagePath);
        return new EdgePluginVersionRelease(
            "TestPlugin",
            "测试插件",
            null,
            null,
            null,
            new EdgePluginVersionEntry(
                Guid.NewGuid(),
                "stable",
                "1.2.0",
                EdgeClientHostRuntime.HostApiVersion,
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                packagePath,
                ComputeSha256(packagePath),
                new FileInfo(packagePath).Length,
                null,
                [],
                "Published",
                null,
                "IIoT",
                DateTime.UtcNow,
                DateTime.UtcNow));
    }

    private static void CreatePluginPackage(
        string path,
        string moduleId,
        string version,
        string? extraEntryName = null)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "plugin.json",
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "测试插件",
              "version": "{{version}}",
              "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "IIoT.Edge.TestPlugin.dll",
              "entryType": "IIoT.Edge.TestPlugin.DependencyInjection",
              "supportedProcessType": "{{moduleId}}",
              "dependencies": []
            }
            """);
        WriteEntry(archive, "IIoT.Edge.TestPlugin.dll", "dummy");
        if (!string.IsNullOrWhiteSpace(extraEntryName))
        {
            WriteEntry(archive, extraEntryName, "bad");
        }
    }

    private static void CreatePluginPackageWithInvalidActivation(
        string path,
        string moduleId,
        string version)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "plugin.json",
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "测试插件",
              "version": "{{version}}",
              "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "IIoT.Edge.TestPlugin.dll",
              "entryType": "IIoT.Edge.TestPlugin.DependencyInjection",
              "supportedProcessType": "{{moduleId}}",
              "dependencies": []
            }
            """);
        WriteEntry(archive, "IIoT.Edge.TestPlugin.dll", "dummy");
        WriteEntry(
            archive,
            "activation/manifest.json",
            $$"""
            {
              "schemaVersion": 1,
              "moduleId": "{{moduleId}}",
              "profiles": [
                {
                  "profileId": "{{moduleId}}",
                  "launcherProfile": "launcher.profile.json",
                  "machineConfig": "appsettings.machine.json"
                }
              ]
            }
            """);
        WriteEntry(
            archive,
            "activation/launcher.profile.json",
            $$"""
            [
              {
                "profileId": "{{moduleId}}",
                "displayName": "{{moduleId}}",
                "machineProfile": "{{moduleId}}",
                "executablePath": "../host/IIoT.Edge.Shell"
              }
            ]
            """);
        WriteEntry(
            archive,
            "activation/appsettings.machine.json",
            $$"""
            {
              "InstanceId": "{{moduleId}}",
              "Shell": {
                "MachineProfile": "{{moduleId}}"
              },
              "CloudApi": {
                "ClientCode": "must-not-ship",
                "BootstrapSecret": "must-not-ship"
              },
              "Modules": {
                "Enabled": [ "{{moduleId}}" ],
                "{{moduleId}}": {}
              }
            }
            """);
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CreateShaIfMissing(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("测试插件包不存在。", path);
        }
    }

    private static void WithDataRoot(string dataRoot, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, previous);
        }
    }

    private static void WithEnvironmentVariables(
        IReadOnlyDictionary<string, string?> values,
        Action action)
    {
        var previous = values.Keys.ToDictionary(
            static key => key,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }

        try
        {
            action();
        }
        finally
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    private static async Task WithDataRootAsync(string dataRoot, Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        try
        {
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, previous);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-update-infrastructure-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class MissingCloudConfigurationProvider : IEdgeUpdateConfigurationProvider
    {
        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
            => EdgeUpdateConfigurationResult.Failed("CloudApi 配置不完整。");

        public EdgeReleaseOptions ResolveReleaseOptions()
            => new("stable", "win-x64");
    }

    private sealed class SuccessfulCloudConfigurationProvider : IEdgeUpdateConfigurationProvider
    {
        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
            => EdgeUpdateConfigurationResult.Succeeded(CloudOptions());

        public EdgeReleaseOptions ResolveReleaseOptions()
            => new("stable", "win-x64");
    }

    private sealed class ProfileCloudConfigurationProvider(
        IReadOnlyDictionary<string, EdgeUpdateCloudApiOptions> optionsByProfile)
        : IEdgeUpdateConfigurationProvider
    {
        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
            => optionsByProfile.TryGetValue(target.MachineProfile, out var options)
                ? EdgeUpdateConfigurationResult.Succeeded(options)
                : EdgeUpdateConfigurationResult.Failed(
                    $"Missing profile configuration: {target.MachineProfile}");

        public EdgeReleaseOptions ResolveReleaseOptions()
            => new("stable", "win-x64");
    }

    private sealed class SuccessfulDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Succeeded(
                new EdgeUpdateDeviceSession(Guid.NewGuid(), "测试设备", options.ClientCode, "token")));
    }

    private sealed class RecordingDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public int BootstrapCallCount { get; private set; }

        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
        {
            BootstrapCallCount++;
            return Task.FromResult(
                EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Succeeded(
                    new EdgeUpdateDeviceSession(
                        Guid.NewGuid(),
                        "测试设备",
                        options.ClientCode,
                        "token")));
        }
    }

    private sealed class FixedCatalogClient(EdgeReleaseCatalog catalog) : IEdgeUpdateCatalogClient
    {
        public Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeUpdateOperationResult<EdgeReleaseCatalog>.Succeeded(catalog));
    }

    private sealed class ProfileCatalogClient(
        IReadOnlyDictionary<string, EdgeReleaseCatalog> catalogsByBaseUrl)
        : IEdgeUpdateCatalogClient
    {
        public Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                catalogsByBaseUrl.TryGetValue(options.BaseUrl, out var catalog)
                    ? EdgeUpdateOperationResult<EdgeReleaseCatalog>.Succeeded(catalog)
                    : EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(
                        $"Missing catalog: {options.BaseUrl}"));
    }

    private sealed class RecordingCatalogClient(EdgeReleaseCatalog catalog)
        : IEdgeUpdateCatalogClient
    {
        public int CatalogCallCount { get; private set; }

        public Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            CancellationToken cancellationToken = default)
        {
            CatalogCallCount++;
            return Task.FromResult(
                EdgeUpdateOperationResult<EdgeReleaseCatalog>.Succeeded(catalog));
        }
    }

    private sealed class FailedCatalogClient(string error) : IEdgeUpdateCatalogClient
    {
        public Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(error));
    }

    private sealed class NotCalledDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Cloud bootstrap should not be called.");
    }

    private sealed class NotCalledCatalogClient : IEdgeUpdateCatalogClient
    {
        public Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Cloud catalog should not be called.");
    }

    private sealed class NoopVersionReporter : IEdgeVersionReporter
    {
        public Task<EdgeVersionReportResult> ReportVersionAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            EdgeUpdateTarget target,
            string hostVersion,
            string hostApiVersion,
            IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
            IReadOnlyList<string> enabledPlugins,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());
    }

    private sealed class RecordingVersionReporter : IEdgeVersionReporter
    {
        public int ReportCallCount { get; private set; }

        public Task<EdgeVersionReportResult> ReportVersionAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeReleaseOptions releaseOptions,
            EdgeUpdateTarget target,
            string hostVersion,
            string hostApiVersion,
            IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
            IReadOnlyList<string> enabledPlugins,
            CancellationToken cancellationToken = default)
        {
            ReportCallCount++;
            return Task.FromResult(EdgeVersionReportResult.Succeeded());
        }
    }

    private sealed class EmptyProfileModuleConfigurationStore : IEdgeProfileModuleConfigurationStore
    {
        public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
            => [];

        public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
        {
        }
    }

    private sealed class FixedInstalledPluginCatalog(
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins)
        : IEdgeInstalledPluginCatalog
    {
        public IReadOnlyList<EdgeInstalledPlugin> LoadInstalledPlugins(
            EdgeUpdateTarget target)
            => installedPlugins;
    }

    private sealed class FixedProfileModuleConfigurationStore(IReadOnlyList<string> enabledModules)
        : IEdgeProfileModuleConfigurationStore
    {
        public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
            => enabledModules;

        public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
        {
        }
    }

    private sealed class ProfileModuleConfigurationStore(
        IReadOnlyDictionary<string, IReadOnlyList<string>> modulesByProfile)
        : IEdgeProfileModuleConfigurationStore
    {
        public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
            => modulesByProfile.TryGetValue(target.MachineProfile, out var modules)
                ? modules
                : [];

        public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
        {
        }
    }

    private sealed class FixedCloudSwitchReader(bool enabled) : IEdgeProfileCloudSwitchReader
    {
        public bool IsEnabled(EdgeUpdateTarget target) => enabled;
    }

    private sealed class EnabledCloudSwitchReader : IEdgeProfileCloudSwitchReader
    {
        public static EnabledCloudSwitchReader Instance { get; } = new();

        public bool IsEnabled(EdgeUpdateTarget target) => true;
    }

    private sealed class NoopPluginPackageInstaller : IEdgePluginPackageInstaller
    {
        public Task<EdgePluginInstallResult> InstallAsync(
            EdgeUpdateTarget target,
            EdgePluginVersionRelease release,
            EdgeUpdateCloudApiOptions cloudOptions,
            string hostVersion,
            string hostApiVersion,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("No package install in test."));
    }

    private sealed class RecordingPluginPackageInstaller : IEdgePluginPackageInstaller
    {
        public int InstallCallCount { get; private set; }

        public Task<EdgePluginInstallResult> InstallAsync(
            EdgeUpdateTarget target,
            EdgePluginVersionRelease release,
            EdgeUpdateCloudApiOptions cloudOptions,
            string hostVersion,
            string hostApiVersion,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            return Task.FromResult(EdgePluginInstallResult.Succeeded([release.ModuleId]));
        }
    }

    private sealed class RecordingCompositionTransaction(
        EdgePluginInstallResult? rollbackResult = null)
        : IEdgePluginCompositionTransaction
    {
        public int InstallCallCount { get; private set; }

        public int RollbackCallCount { get; private set; }

        public IReadOnlyList<EdgePluginCompositionRelease> Releases { get; private set; } = [];

        public string? PendingHostVersion { get; private set; }

        public Task<EdgePluginInstallResult> InstallAsync(
            IReadOnlyList<EdgePluginCompositionTarget> targets,
            IReadOnlyList<EdgePluginCompositionRelease> releases,
            string compatibilityHostVersion,
            string compatibilityHostApiVersion,
            string? pendingHostVersion,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCallCount++;
            Releases = releases;
            PendingHostVersion = pendingHostVersion;
            return Task.FromResult(
                EdgePluginInstallResult.Succeeded(
                    releases
                        .Select(static release => release.Release.ModuleId)
                        .ToArray()));
        }

        public EdgePluginInstallResult RollbackPendingHostHandoff()
        {
            RollbackCallCount++;
            return rollbackResult ?? EdgePluginInstallResult.Succeeded([]);
        }
    }

    private sealed class NoopHostUpdateService : IEdgeHostUpdateService
    {
        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NotConfigured));

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "No host update in test."));

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "No host update in test."));
    }

    private sealed class RecordingHostUpdateService : IEdgeHostUpdateService
    {
        public int ApplyCallCount { get; private set; }

        public string? AppliedVersion { get; private set; }

        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.UpdateAvailable));

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(true));

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            AppliedVersion = release.Version.Version;
            return Task.FromResult(new EdgeHostUpdateApplyResult(true));
        }
    }

    private sealed class FailingHostUpdateService : IEdgeHostUpdateService
    {
        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EdgeHostUpdateCheckResult(
                    EdgeHostUpdateCheckState.UpdateAvailable));

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EdgeHostUpdateApplyResult(false, "handoff failed"));

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EdgeHostUpdateApplyResult(false, "handoff failed"));
    }

    private sealed class CancelingHostUpdateService(
        CancellationTokenSource cancellation) : IEdgeHostUpdateService
    {
        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new EdgeHostUpdateCheckResult(
                    EdgeHostUpdateCheckState.UpdateAvailable));

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.FromCanceled<EdgeHostUpdateApplyResult>(cancellation.Token);
        }

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return Task.FromCanceled<EdgeHostUpdateApplyResult>(cancellation.Token);
        }
    }
}
