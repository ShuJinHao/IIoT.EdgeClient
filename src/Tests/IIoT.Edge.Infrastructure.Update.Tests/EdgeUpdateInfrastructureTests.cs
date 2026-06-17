using System.IO.Compression;
using System.Security.Cryptography;
using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Infrastructure.Update.Configuration;
using IIoT.Edge.Infrastructure.Update.Host;
using IIoT.Edge.Infrastructure.Update.Packages;
using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;
using Xunit;

namespace IIoT.Edge.Infrastructure.Update.Tests;

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

                var provider = new FileEdgeUpdateConfigurationProvider(hostDirectory);
                var target = Target(hostDirectory);

                var result = provider.Resolve(target);
                var releaseOptions = provider.ResolveReleaseOptions();

                Assert.True(result.Success);
                Assert.Equal("EDGE-001", result.Options!.ClientCode);
                Assert.Equal("secret", result.Options.BootstrapSecret);
                Assert.Equal("/api/v1/edge/client-releases/version-reports", result.Options.ClientVersionReportPath);
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
                    "Enabled": [ "Homogenization" ]
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

                Assert.Equal(["Homogenization", "Welding"], enabled);
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
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.Homogenization-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "Homogenization", "1.2.0");
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
                    "Homogenization");
                Assert.True(result.Success, result.ErrorMessage);
                Assert.True(File.Exists(Path.Combine(pluginDirectory, "plugin.json")));
                Assert.True(File.Exists(Path.Combine(pluginDirectory, "IIoT.Edge.Module.Homogenization.dll")));
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
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.Homogenization-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "Homogenization", "1.2.0", "..\\evil.dll");
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
            var packagePath = Path.Combine(tempDirectory, "IIoT.EdgePlugin.Homogenization-1.2.0-win-x64.zip");
            CreatePluginPackage(packagePath, "Homogenization", "1.2.0");
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
            PluginComponent("Homogenization", Release("Homogenization", "1.2.0", EdgeClientHostRuntime.HostApiVersion)),
            PluginComponent("Welding", Release("Welding", "1.0.0", "2.0.0")));
        var installed = new[]
        {
            new EdgeInstalledPlugin(
                "Homogenization",
                "Homogenization",
                "匀浆",
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

        var homogenization = plans.Single(x => x.ModuleId == "Homogenization");
        var welding = plans.Single(x => x.ModuleId == "Welding");
        Assert.Equal(EdgeVersionStatus.Newer, homogenization.Versions.Single().Status);
        Assert.Equal(EdgeVersionStatus.Incompatible, welding.Versions.Single().Status);
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
            "/api/v1/edge/client-releases/version-reports");

    private static EdgeReleaseCatalog Catalog(params EdgePluginReleaseComponent[] plugins)
        => new(
            2,
            "stable",
            "win-x64",
            new EdgeHostReleaseComponent(
                "Host",
                "Edge Host",
                [
                    new EdgeHostVersionEntry(
                        Guid.NewGuid(),
                        "stable",
                        "1.0.0",
                        EdgeClientHostRuntime.HostApiVersion,
                        "win-x64",
                        "net10.0",
                        "https://cloud.example.test/host.nupkg",
                        new string('A', 64),
                        1,
                        null,
                        "Published",
                        null,
                        "IIoT",
                        DateTime.UtcNow,
                        DateTime.UtcNow)
                ]),
            plugins,
            DateTime.UtcNow);

    private static EdgePluginReleaseComponent PluginComponent(
        string moduleId,
        EdgePluginVersionEntry release)
        => new(
            "Plugin",
            moduleId,
            moduleId,
            null,
            null,
            null,
            [release]);

    private static EdgePluginVersionEntry Release(string moduleId, string version, string hostApiVersion)
        => new(
            Guid.NewGuid(),
            "stable",
            version,
            hostApiVersion,
            "1.0.0",
            "99.0.0",
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

    private static EdgePluginVersionRelease ReleaseWithPackage(string packagePath)
    {
        CreateShaIfMissing(packagePath);
        return new EdgePluginVersionRelease(
            "Homogenization",
            "匀浆",
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
              "displayName": "匀浆",
              "version": "{{version}}",
              "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "IIoT.Edge.Module.Homogenization.dll",
              "entryType": "IIoT.Edge.Module.Homogenization.DependencyInjection",
              "supportedProcessType": "{{moduleId}}",
              "dependencies": []
            }
            """);
        WriteEntry(archive, "IIoT.Edge.Module.Homogenization.dll", "dummy");
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
}
