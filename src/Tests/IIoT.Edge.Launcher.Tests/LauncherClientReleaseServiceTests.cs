using System.IO.Compression;
using System.Security.Cryptography;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherClientReleaseServiceTests
{
    [Fact]
    public void CloudApiConfigurationResolver_ShouldReadExternalProfileIdentityAndReleaseOptions()
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

                var resolver = new LauncherCloudApiConfigurationResolver(hostDirectory);
                var profile = Profile(hostDirectory);

                var result = resolver.Resolve(profile);
                var releaseOptions = resolver.ResolveReleaseOptions();

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
    public void CloudApiConfigurationResolver_ShouldReportIncompleteWithoutBootstrapSecret()
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
                // 只有 ClientCode、缺 BootstrapSecret
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory),
                    """
                    {
                      "CloudApi": {
                        "ClientCode": "DEV-AAAAAAAAAA"
                      }
                    }
                    """);

                var resolver = new LauncherCloudApiConfigurationResolver(hostDirectory);

                var result = resolver.Resolve(Profile(hostDirectory));

                // 方案 B 为密钥模式：缺 BootstrapSecret 必须判定为配置不完整（与 Shell bootstrap 要求一致）
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
                var profile = Profile(hostDirectory);
                var configuration = new LauncherProfileModuleConfiguration();

                configuration.EnableModules(profile, ["Welding"]);

                var enabled = configuration.ReadEnabledModules(profile);
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
            var sha256 = ComputeSha256(packagePath);

            WithDataRoot(dataRoot, async () =>
            {
                var release = new LauncherClientPluginRelease(
                    Guid.NewGuid(),
                    "Homogenization",
                    "匀浆",
                    null,
                    null,
                    null,
                    "stable",
                    "1.2.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "1.0.0",
                    "99.0.0",
                    "win-x64",
                    "net10.0",
                    packagePath,
                    sha256,
                    new FileInfo(packagePath).Length,
                    null,
                    [],
                    "Published",
                    null,
                    "IIoT",
                    DateTime.UtcNow,
                    DateTime.UtcNow);
                var installer = new LauncherPluginPackageInstaller();

                var result = await installer.InstallAsync(
                    Profile(hostDirectory),
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
                Assert.True(File.Exists(Path.Combine(
                    pluginDirectory,
                    "install.json")));
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
            var sha256 = ComputeSha256(packagePath);

            WithDataRoot(dataRoot, async () =>
            {
                var release = new LauncherClientPluginRelease(
                    Guid.NewGuid(),
                    "Homogenization",
                    "匀浆",
                    null,
                    null,
                    null,
                    "stable",
                    "1.2.0",
                    EdgeClientHostRuntime.HostApiVersion,
                    "1.0.0",
                    "99.0.0",
                    "win-x64",
                    "net10.0",
                    packagePath,
                    sha256,
                    new FileInfo(packagePath).Length,
                    null,
                    [],
                    "Published",
                    null,
                    "IIoT",
                    DateTime.UtcNow,
                    DateTime.UtcNow);
                var installer = new LauncherPluginPackageInstaller();

                var result = await installer.InstallAsync(
                    Profile(hostDirectory),
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
            var installer = new LauncherPluginPackageInstaller(
                new HttpClient(),
                LauncherPluginPackageInstallLimits.Default with
                {
                    MaxFileCount = 1
                });

            WithDataRoot(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Profile(hostDirectory),
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
    public async Task PluginPackageInstaller_ShouldRejectPackageWhenExtractedSizeExceedsLimit()
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
            var installer = new LauncherPluginPackageInstaller(
                new HttpClient(),
                LauncherPluginPackageInstallLimits.Default with
                {
                    MaxExtractedBytes = 16
                });

            WithDataRoot(dataRoot, async () =>
            {
                var result = await installer.InstallAsync(
                    Profile(hostDirectory),
                    release,
                    CloudOptions(),
                    "1.0.0",
                    EdgeClientHostRuntime.HostApiVersion);

                Assert.False(result.Success);
                Assert.Contains("解压后大小超过限制", result.ErrorMessage, StringComparison.Ordinal);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void BuildPluginPlans_ShouldMarkUpdateAvailableAndIncompatible()
    {
        var releases = new[]
        {
            Release("Homogenization", "1.2.0", EdgeClientHostRuntime.HostApiVersion),
            Release("Welding", "1.0.0", "2.0.0")
        };
        var installed = new[]
        {
            new LauncherInstalledPlugin(
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

        var plans = LauncherClientReleaseService.BuildPluginPlans(
            releases,
            installed,
            "1.0.0",
            EdgeClientHostRuntime.HostApiVersion);

        Assert.Equal(LauncherPluginUpdateState.UpdateAvailable, plans.Single(x => x.Release.ModuleId == "Homogenization").State);
        Assert.Equal(LauncherPluginUpdateState.Incompatible, plans.Single(x => x.Release.ModuleId == "Welding").State);
    }

    private static LauncherProfileDefinition Profile(string hostDirectory)
        => new(
            "LineA",
            "Line A",
            "Line A profile",
            null,
            "LineA",
            Path.Combine(hostDirectory, "IIoT.Edge.Shell"),
            "Cog",
            "#0F766E");

    private static LauncherCloudApiOptions CloudOptions()
        => new(
            "https://cloud.example.test",
            5,
            "EDGE-001",
            "secret",
            "/api/v1/bootstrap/device-instance",
            "/api/v1/edge/client-releases/device/{deviceId}/catalog",
            "/api/v1/edge/client-releases/version-reports");

    private static LauncherClientPluginRelease Release(string moduleId, string version, string hostApiVersion)
        => new(
            Guid.NewGuid(),
            moduleId,
            moduleId,
            null,
            null,
            null,
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

    private static LauncherClientPluginRelease ReleaseWithPackage(string packagePath)
        => new(
            Guid.NewGuid(),
            "Homogenization",
            "匀浆",
            null,
            null,
            null,
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
            DateTime.UtcNow);

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

    private static void WithDataRoot(string dataRoot, Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        try
        {
            action().GetAwaiter().GetResult();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, previous);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-launcher-client-release-tests", Guid.NewGuid().ToString("N"));
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
