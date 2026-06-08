using IIoT.Edge.Launcher;
using IIoT.Edge.Launcher.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherDependencyInjectionTests
{
    [Fact]
    public void AddLauncherServices_ShouldRegisterRequiredServices()
    {
        var services = new ServiceCollection();
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            services.AddLauncherServices(baseDirectory);

            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            Assert.IsType<LauncherProfileCatalog>(provider.GetRequiredService<ILauncherProfileCatalog>());
            Assert.IsType<ProcessStarter>(provider.GetRequiredService<IProcessStarter>());
            Assert.IsType<ShellLaunchService>(provider.GetRequiredService<IShellLaunchService>());
            Assert.IsType<LauncherAccountCatalogInitializer>(
                provider.GetRequiredService<ILauncherAccountCatalogInitializer>());
            Assert.IsType<LauncherUpdateConfigInitializer>(
                provider.GetRequiredService<ILauncherUpdateConfigInitializer>());
            Assert.IsType<LauncherAccountCatalog>(provider.GetRequiredService<ILauncherAccountCatalog>());
            Assert.IsType<LocalLauncherAuthService>(provider.GetRequiredService<ILocalLauncherAuthService>());
            Assert.IsType<LauncherCloudApiConfigurationResolver>(
                provider.GetRequiredService<ILauncherCloudApiConfigurationResolver>());
            Assert.IsType<LauncherEdgeReleaseCloudClient>(
                provider.GetRequiredService<ILauncherEdgeReleaseCloudClient>());
            Assert.IsType<LauncherInstalledPluginCatalog>(
                provider.GetRequiredService<ILauncherInstalledPluginCatalog>());
            Assert.IsType<LauncherProfileModuleConfiguration>(
                provider.GetRequiredService<ILauncherProfileModuleConfiguration>());
            Assert.IsType<LauncherPluginPackageInstaller>(
                provider.GetRequiredService<ILauncherPluginPackageInstaller>());
            Assert.IsType<LauncherClientReleaseService>(
                provider.GetRequiredService<ILauncherClientReleaseService>());
        }
        finally
        {
            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_ShouldCreateDefaultAdminAccount()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(
                LauncherAccountCatalog.GetCatalogPath(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName),
                """
                [
                  {
                    "userName": "admin",
                    "displayName": "本地管理员",
                    "passwordHash": "hash",
                    "isEnabled": true
                  }
                ]
                """);

            var catalog = new LauncherAccountCatalog(tempDirectory);
            var initializer = new LauncherAccountCatalogInitializer(tempDirectory);

            initializer.EnsureCatalogExists();

            var accounts = catalog.LoadAccounts();

            var account = Assert.Single(accounts);
            Assert.Equal("admin", account.UserName);
            Assert.Equal("本地管理员", account.DisplayName);
            Assert.True(account.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(account.PasswordHash));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LauncherAccountCatalog_ShouldRoundTripAccounts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var path = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
            await File.WriteAllTextAsync(
                path,
                """
                [
                  {
                    "userName": "operator",
                    "displayName": "操作员",
                    "passwordHash": "hash",
                    "isEnabled": true
                  }
                ]
                """);

            var catalog = new LauncherAccountCatalog(tempDirectory);
            var loaded = catalog.LoadAccounts();

            var account = Assert.Single(loaded);
            Assert.Equal("operator", account.UserName);
            Assert.Equal("操作员", account.DisplayName);
            Assert.Equal("hash", account.PasswordHash);
            Assert.True(account.IsEnabled);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_WhenCatalogExists_ShouldNotOverwriteExistingAccounts()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var accountsPath = Path.Combine(tempDirectory, "protected", LauncherAccountCatalog.DefaultCatalogFileName);
            var samplePath = Path.Combine(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(accountsPath)!);
            File.WriteAllText(
                accountsPath,
                """
                [
                  {
                    "userName": "operator",
                    "displayName": "现场账号",
                    "passwordHash": "protected-hash",
                    "isEnabled": true
                  }
                ]
                """);
            File.WriteAllText(
                samplePath,
                """
                [
                  {
                    "userName": "admin",
                    "displayName": "样例账号",
                    "passwordHash": "sample-hash",
                    "isEnabled": true
                  }
                ]
                """);
            var originalAccounts = File.ReadAllText(accountsPath);

            var initializer = new LauncherAccountCatalogInitializer(
                new LauncherAccountCatalogPaths(accountsPath, samplePath));

            initializer.EnsureCatalogExists();

            Assert.Equal(originalAccounts, File.ReadAllText(accountsPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherAccountCatalogInitializer_WhenSampleIsMissing_ShouldNotBlockStartup()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var accountsPath = Path.Combine(tempDirectory, "protected-data", LauncherAccountCatalog.DefaultCatalogFileName);
            var samplePath = Path.Combine(tempDirectory, LauncherAccountCatalog.SampleCatalogFileName);

            var initializer = new LauncherAccountCatalogInitializer(
                new LauncherAccountCatalogPaths(accountsPath, samplePath));

            initializer.EnsureCatalogExists();

            Assert.False(File.Exists(accountsPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateConfigInitializer_ShouldCreateConfigFromSample()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "protected-data", "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, LauncherUpdateConfigInitializer.SampleConfigFileName);
            File.WriteAllText(samplePath, """{"Source": ""}""");

            var initializer = new LauncherUpdateConfigInitializer(
                new LauncherUpdateConfigPaths(configPath, samplePath));

            initializer.EnsureConfigExists();

            Assert.True(File.Exists(configPath));
            Assert.Equal(File.ReadAllText(samplePath), File.ReadAllText(configPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateConfigInitializer_WhenConfigExists_ShouldNotOverwriteExistingConfig()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var configPath = Path.Combine(tempDirectory, "protected-data", "launcher.update.json");
            var samplePath = Path.Combine(tempDirectory, LauncherUpdateConfigInitializer.SampleConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, """{"Source": "http://existing.example/updates"}""");
            File.WriteAllText(samplePath, """{"Source": ""}""");
            var originalConfig = File.ReadAllText(configPath);

            var initializer = new LauncherUpdateConfigInitializer(
                new LauncherUpdateConfigPaths(configPath, samplePath));

            initializer.EnsureConfigExists();

            Assert.Equal(originalConfig, File.ReadAllText(configPath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateService_WhenSourceIsLocalDirectory_ShouldResolveLocalDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);

            var localDirectory = LauncherUpdateService.TryResolveLocalDirectory(tempDirectory);

            Assert.NotNull(localDirectory);
            Assert.Equal(Path.GetFullPath(tempDirectory), localDirectory.FullName);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateService_WhenSourceIsFileUriDirectory_ShouldResolveLocalDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"iiot-launcher-test-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            var sourceUri = new Uri(tempDirectory).AbsoluteUri;

            var localDirectory = LauncherUpdateService.TryResolveLocalDirectory(sourceUri);

            Assert.NotNull(localDirectory);
            Assert.Equal(Path.GetFullPath(tempDirectory), localDirectory.FullName);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void LauncherUpdateService_WhenSourceIsWebUrl_ShouldNotResolveLocalDirectory()
    {
        var localDirectory = LauncherUpdateService.TryResolveLocalDirectory("https://updates.example/edge/");

        Assert.Null(localDirectory);
    }
}
