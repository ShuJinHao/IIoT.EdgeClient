using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherDeviceBindingImporterTests
{
    [Fact]
    public void ApplyPendingBindings_ShouldReadPendingBindingFromProgramDataLauncherDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "install", "current");
            var hostDirectory = Path.Combine(currentDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": {
                    "Enabled": false,
                    "ClientCode": "",
                    "BootstrapSecret": "",
                    "TimeoutSecs": 17,
                    "Paths": {
                      "DeviceInstance": "/site/old-device-instance",
                      "SiteOwned": "/site/keep"
                    }
                  },
                  "Site": { "Keep": true },
                  "Modules": { "Enabled": [ "TestPlugin" ] }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDir = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentDirectory);
                var pendingPath = Path.Combine(launcherDir, LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    """
                    {
                      "schemaVersion": 2,
                      "baseUrl": "http://cloud.local:81",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "TestPlugin",
                          "clientCode": "DEV-AAAAAAAAAA",
                          "bootstrapSecret": "SEC-HOMOG-001",
                          "deviceName": "测试设备",
                          "processId": "11111111-1111-1111-1111-111111111111"
                        }
                      ]
                    }
                    """);

                var credentialStore = new FakeCredentialStore();
                var importer = new LauncherDeviceBindingImporter(
                    currentDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory(),
                    credentialStore: credentialStore);

                importer.ApplyPendingBindings();

                var externalConfig = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                        "DEV-AAAAAAAAAA",
                        currentDirectory));
                Assert.Contains("\"Enabled\": true", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"BaseUrl\": \"http://cloud.local:81\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"ClientCode\": \"DEV-AAAAAAAAAA\"", externalConfig, StringComparison.Ordinal);
                Assert.DoesNotContain("BootstrapSecret", externalConfig, StringComparison.Ordinal);
                Assert.Contains("BootstrapCredentialReference", externalConfig, StringComparison.Ordinal);
                Assert.Equal("SEC-HOMOG-001", Assert.Single(credentialStore.Values).Value);
                Assert.Contains("\"DeviceInstance\": \"/api/v1/bootstrap/device-instance\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"ClientReleaseCatalogTemplate\": \"/api/v1/edge/client-releases/device/{deviceId}/catalog\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"ClientVersionReport\": \"/api/v1/edge/client-releases/version-reports\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"RuntimeHeartbeat\": \"/api/v1/edge/runtime-heartbeats\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"TimeoutSecs\": 17", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"SiteOwned\": \"/site/keep\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"Keep\": true", externalConfig, StringComparison.Ordinal);
                Assert.False(File.Exists(pendingPath));

                var appliedFiles = Directory.GetFiles(launcherDir, "iiot-binding.applied.*.json");
                Assert.Single(appliedFiles);
                var runtimeBinding = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(currentDirectory));
                Assert.DoesNotContain("SEC-HOMOG-001", runtimeBinding, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_WithApAndCp_ShouldWriteBothExternalMachineConfigs()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """{ "Modules": { "Enabled": [ "AP" ] } }""");
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineB.json"),
                """{ "Modules": { "Enabled": [ "CP" ] } }""");

            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory =
                    EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(
                    launcherDirectory,
                    LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    """
                    {
                      "schemaVersion": 2,
                      "baseUrl": "https://cloud.example.test",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "AP",
                          "clientCode": "DEV-AP",
                          "bootstrapSecret": "SECRET-AP",
                          "deviceName": "AP 设备",
                          "processId": "11111111-1111-1111-1111-111111111111"
                        },
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "SECRET-CP",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """);

                var credentialStore = new FakeCredentialStore();
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(
                        Profile(hostDirectory, "LineA"),
                        Profile(hostDirectory, "LineB")),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory(),
                    credentialStore: credentialStore);

                importer.ApplyPendingBindings();

                var apConfig = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                        "DEV-AP",
                        hostDirectory));
                var cpConfig = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                        "DEV-CP",
                        hostDirectory));
                Assert.Contains("\"ClientCode\": \"DEV-AP\"", apConfig, StringComparison.Ordinal);
                Assert.Contains("\"ClientCode\": \"DEV-CP\"", cpConfig, StringComparison.Ordinal);
                Assert.Contains("\"RuntimeHeartbeat\": \"/api/v1/edge/runtime-heartbeats\"", apConfig, StringComparison.Ordinal);
                Assert.Contains("\"RuntimeHeartbeat\": \"/api/v1/edge/runtime-heartbeats\"", cpConfig, StringComparison.Ordinal);
                Assert.False(File.Exists(pendingPath));

                var summary = File.ReadAllText(Assert.Single(
                    Directory.GetFiles(
                        launcherDirectory,
                        "iiot-binding.applied.*.json")));
                Assert.Contains("DEV-AP", summary, StringComparison.Ordinal);
                Assert.Contains("DEV-CP", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("SECRET-AP", summary, StringComparison.Ordinal);
                Assert.DoesNotContain("SECRET-CP", summary, StringComparison.Ordinal);
                Assert.Contains(credentialStore.Values, pair =>
                    pair.Key.EndsWith("/DEV-AP", StringComparison.Ordinal)
                    && pair.Value == "SECRET-AP");
                Assert.Contains(credentialStore.Values, pair =>
                    pair.Key.EndsWith("/DEV-CP", StringComparison.Ordinal)
                    && pair.Value == "SECRET-CP");
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldIgnoreBaseDirectoryBinding()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            // 打包机器配置：声明启用模块 TestPlugin（moduleId -> profile 据此匹配）
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": { "ClientCode": "", "BootstrapSecret": "" },
                  "Modules": { "Enabled": [ "TestPlugin" ] }
                }
                """);

            // 旧 exe 目录绑定文件不再属于当前安装契约。
            var legacyBindingPath = Path.Combine(hostDirectory, LauncherDeviceBindingImporter.BindingFileName);
            WriteText(
                legacyBindingPath,
                """
                {
                  "schemaVersion": 1,
                  "baseUrl": "http://cloud.local:81",
                  "bindings": [
                    {
                      "moduleId": "TestPlugin",
                      "clientCode": "DEV-AAAAAAAAAA",
                      "bootstrapSecret": "SEC-HOMOG-001",
                      "deviceName": "测试插件线1#"
                    }
                  ]
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory)));
                Assert.True(File.Exists(legacyBindingPath));

                var launcherDir = EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                Assert.False(Directory.Exists(launcherDir));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldBeNoOpWhenBindingFileMissing()
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
                { "Modules": { "Enabled": [ "TestPlugin" ] } }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                // 无绑定文件：不抛异常、也不创建外部配置
                var exception = Record.Exception(() => importer.ApplyPendingBindings());

                Assert.Null(exception);
                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory)));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_WhenOnlyOnePluginIsInstalled_ShouldKeepUnresolvedBindingPending()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "install", "current");
            var hostDirectory = Path.Combine(currentDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": { "ClientCode": "", "BootstrapSecret": "" },
                  "Modules": { "Enabled": [ "AP" ] }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentDirectory);
                var pendingPath = Path.Combine(launcherDirectory, LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    """
                    {
                      "schemaVersion": 2,
                      "baseUrl": "http://cloud.local:81",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "AP",
                          "clientCode": "DEV-AP",
                          "bootstrapSecret": "SECRET-AP",
                          "deviceName": "AP 设备",
                          "processId": "11111111-1111-1111-1111-111111111111"
                        },
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "SECRET-CP",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """);

                var credentialStore = new FakeCredentialStore();
                var importer = new LauncherDeviceBindingImporter(
                    currentDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory(),
                    credentialStore: credentialStore);

                importer.ApplyPendingBindings();

                var pending = File.ReadAllText(pendingPath);
                Assert.Contains("DEV-AP", pending, StringComparison.Ordinal);
                Assert.Contains("SECRET-AP", pending, StringComparison.Ordinal);
                Assert.Contains("DEV-CP", pending, StringComparison.Ordinal);
                Assert.Contains("SECRET-CP", pending, StringComparison.Ordinal);
                Assert.Empty(Directory.GetFiles(launcherDirectory, "iiot-binding.applied.*.json"));
                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                        "DEV-AP",
                        currentDirectory)));
                Assert.Empty(credentialStore.Values);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_WhenNoPluginMatches_ShouldLeavePendingFileUntouched()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(launcherDirectory, LauncherDeviceBindingImporter.BindingFileName);
                const string pendingJson =
                    """
                    {
                      "schemaVersion": 2,
                      "baseUrl": "http://cloud.local:81",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "SECRET-CP",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """;
                WriteText(pendingPath, pendingJson);

                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.Equal(pendingJson, File.ReadAllText(pendingPath));
                Assert.Empty(Directory.GetFiles(launcherDirectory, "iiot-binding.applied.*.json"));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldNotThrowOnCorruptBindingFile()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory =
                    EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(
                    launcherDirectory,
                    LauncherDeviceBindingImporter.BindingFileName);
                WriteText(pendingPath, "{ not valid json");
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                // 启动红线：JSON 损坏不得抛 fatal
                var exception = Record.Exception(() => importer.ApplyPendingBindings());

                Assert.Null(exception);
                Assert.True(File.Exists(pendingPath));
                Assert.Empty(Directory.GetFiles(
                    launcherDirectory,
                    "iiot-binding.applied.*.json"));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Theory]
    [InlineData(1, true, "/api/v1/edge/client-releases/device/{deviceId}/catalog", "2026-08-03T00:00:00Z")]
    [InlineData(2, false, "/api/v1/edge/client-releases/device/{deviceId}/catalog", "2026-08-03T00:00:00Z")]
    [InlineData(2, true, "/api/v1/edge/client-releases/device/catalog", "2026-08-03T00:00:00Z")]
    [InlineData(2, true, "/api/v1/%2e%2e/device/{deviceId}/catalog", "2026-08-03T00:00:00Z")]
    [InlineData(2, true, "/api/v1/edge/client-releases/device/{deviceId}/catalog", "2026-08-03T00:00:00")]
    public void ApplyPendingBindings_WhenSchemaOrPathsAreUnsupported_ShouldPerformZeroWrites(
        int schemaVersion,
        bool includeRuntimeHeartbeat,
        string catalogTemplate,
        string generatedAtUtc)
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
                  "CloudApi": { "Enabled": false },
                  "Modules": { "Enabled": [ "CP" ] }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory =
                    EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(
                    launcherDirectory,
                    LauncherDeviceBindingImporter.BindingFileName);
                var runtimeHeartbeat = includeRuntimeHeartbeat
                    ? "\"runtimeHeartbeat\": \"/api/v1/edge/runtime-heartbeats\""
                    : "\"unrelated\": \"/not-the-required-path\"";
                var pendingJson = $$"""
                    {
                      "schemaVersion": {{schemaVersion}},
                      "baseUrl": "https://cloud.example.test",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "{{catalogTemplate}}",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        {{runtimeHeartbeat}}
                      },
                      "generatedAtUtc": "{{generatedAtUtc}}",
                      "bindings": [
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "SECRET-CP",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """;
                WriteText(pendingPath, pendingJson);

                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.Equal(pendingJson, File.ReadAllText(pendingPath));
                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                        "LineA",
                        hostDirectory)));
                Assert.Empty(Directory.GetFiles(
                    launcherDirectory,
                    "iiot-binding.applied.*.json"));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Theory]
    [InlineData("", "SECRET-CP")]
    [InlineData("http://cloud.local", "")]
    [InlineData("file:///tmp/cloud", "SECRET-CP")]
    public void ApplyPendingBindings_WhenCloudBindingIsIncomplete_ShouldKeepPendingAndCloudDisabled(
        string baseUrl,
        string bootstrapSecret)
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
                  "CloudApi": { "Enabled": false, "ClientCode": "", "BootstrapSecret": "" },
                  "Modules": { "Enabled": [ "CP" ] }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(launcherDirectory, LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    $$"""
                    {
                      "schemaVersion": 2,
                      "baseUrl": "{{baseUrl}}",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "{{bootstrapSecret}}",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """);

                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.True(File.Exists(pendingPath));
                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory)));
                Assert.Empty(Directory.GetFiles(launcherDirectory, "iiot-binding.applied.*.json"));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_WhenExternalMachineConfigIsCorrupt_ShouldPreserveItAndPendingBinding()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """{ "Modules": { "Enabled": [ "CP" ] } }""");

            WithDataRoot(dataRoot, () =>
            {
                var externalPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory);
                const string corruptConfig = "{ existing site config is corrupt";
                WriteText(externalPath, corruptConfig);
                var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                var pendingPath = Path.Combine(launcherDirectory, LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    """
                    {
                      "schemaVersion": 2,
                      "baseUrl": "http://cloud.local",
                      "paths": {
                        "deviceInstance": "/api/v1/bootstrap/device-instance",
                        "clientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                        "clientVersionReport": "/api/v1/edge/client-releases/version-reports",
                        "runtimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                      },
                      "generatedAtUtc": "2026-08-03T00:00:00Z",
                      "bindings": [
                        {
                          "moduleId": "CP",
                          "clientCode": "DEV-CP",
                          "bootstrapSecret": "SECRET-CP",
                          "deviceName": "CP 设备",
                          "processId": "22222222-2222-2222-2222-222222222222"
                        }
                      ]
                    }
                    """);

                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.Equal(corruptConfig, File.ReadAllText(externalPath));
                Assert.True(File.Exists(pendingPath));
                Assert.Empty(Directory.GetFiles(launcherDirectory, "iiot-binding.applied.*.json"));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private sealed class FakeProfileCatalog : ILauncherProfileCatalog
    {
        private readonly IReadOnlyList<LauncherProfileDefinition> _profiles;

        public FakeProfileCatalog(params LauncherProfileDefinition[] profiles) => _profiles = profiles;

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => _profiles;
    }

    private sealed class FakeCredentialStore : IEdgeCredentialStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public void Write(string reference, string secret) => Values[reference] = secret;

        public string Read(string reference)
            => Values.TryGetValue(reference, out var secret)
                ? secret
                : throw new KeyNotFoundException(reference);

        public void Delete(string reference) => Values.Remove(reference);
    }

    private static LauncherProfileDefinition Profile(
        string hostDirectory,
        string machineProfile = "LineA")
        => new(
            machineProfile,
            machineProfile,
            "测试 profile",
            null,
            machineProfile,
            Path.Combine(hostDirectory, "IIoT.Edge.Shell"),
            "BeakerOutline",
            "#4D7C0F");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "iiot-edge-binding-tests", Guid.NewGuid().ToString("N"));
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
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, previous);
        }
    }
}
