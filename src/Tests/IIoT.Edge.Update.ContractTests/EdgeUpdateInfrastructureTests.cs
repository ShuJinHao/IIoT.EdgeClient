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
                    "BaseUrl": "https://cloud.example.test",
                    "TimeoutSecs": 7,
                      "Paths": {
                        "DeviceInstance": "/api/v1/bootstrap/device-instance",
                        "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
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
                        "ClientCode": "EDGE-001",
                        "BootstrapSecret": "secret"
                      }
                    }
                    """);
                WriteText(
                    EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(hostDirectory),
                    """
                    {
                      "Channel": "beta",
                      "TargetRuntime": "win-arm64"
                    }
                    """);

                var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory, EnabledCloudSwitchReader.Instance);
                var target = Target(hostDirectory);

                var result = provider.Resolve(target);
                var releaseOptions = provider.ResolveReleaseOptions();

                Assert.True(result.Success);
                Assert.Equal("EDGE-001", result.Options!.ClientCode);
                Assert.Equal("secret", result.Options.BootstrapSecret);
                Assert.Equal("/api/v1/edge/client-releases/version-reports", result.Options.ClientVersionReportPath);
                Assert.Equal("/api/v1/edge/runtime-heartbeats", result.Options.RuntimeHeartbeatPath);
                Assert.Equal("beta", releaseOptions.Channel);
                Assert.Equal("win-arm64", releaseOptions.TargetRuntime);
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
                        "ClientCode": "DEV-AAAAAAAAAA"
                      }
                    }
                    """);

                var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory, EnabledCloudSwitchReader.Instance);

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
    public void ConfigurationProvider_WhenProfileCloudSwitchIsDisabled_ShouldFailBeforeCloudConfiguration()
    {
        var provider = new FileEdgeUpdateConfigurationProvider(
            Path.GetTempPath(),
            new FixedCloudSwitchReader(false));

        var result = provider.Resolve(Target(Path.GetTempPath()));

        Assert.False(result.Success);
        Assert.Contains("Cloud 通信已关闭", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
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
    public void UpdateConfigInitializer_TrySyncUpdateSource_ShouldNormalizeCamelCaseKey()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-update-test-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, "launcher.update.sample.json");
            File.WriteAllText(configPath,
                """{"source": "https://cloud.example.test/edge-updates/velopack/stable/", "channel": "stable"}""");

            var initializer = new FileEdgeUpdateConfigInitializer(new EdgeUpdateConfigPaths(configPath, samplePath));

            var changed = initializer.TrySyncUpdateSource(
                "https://cloud.example.test/edge-updates/velopack/stable/");

            Assert.True(changed);
            var content = File.ReadAllText(configPath);
            Assert.Contains("\"Source\"", content, StringComparison.Ordinal);
            Assert.DoesNotContain("\"source\"", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
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
                new NoopUpdateConfigInitializer(),
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
                new NoopUpdateConfigInitializer(),
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
                new NoopUpdateConfigInitializer(),
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
                    new NoopUpdateConfigInitializer(),
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
        => new(
            "https://cloud.example.test",
            5,
            "EDGE-001",
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

    private sealed class SuccessfulDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Succeeded(
                new EdgeUpdateDeviceSession(Guid.NewGuid(), "测试设备", options.ClientCode, "token")));
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

    private sealed class EmptyProfileModuleConfigurationStore : IEdgeProfileModuleConfigurationStore
    {
        public IReadOnlyList<string> ReadEnabledModules(EdgeUpdateTarget target)
            => [];

        public void EnableModules(EdgeUpdateTarget target, IReadOnlyList<string> moduleIds)
        {
        }
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

    private sealed class NoopUpdateConfigInitializer : IEdgeUpdateConfigInitializer
    {
        public void EnsureConfigExists()
        {
        }

        public bool TrySyncUpdateSource(string updateSource)
            => false;
    }
}
