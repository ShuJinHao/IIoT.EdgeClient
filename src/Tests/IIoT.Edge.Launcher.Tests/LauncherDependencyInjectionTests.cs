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
            Assert.IsType<LauncherAccountCatalog>(provider.GetRequiredService<ILauncherAccountCatalog>());
            Assert.IsType<LocalLauncherAuthService>(provider.GetRequiredService<ILocalLauncherAuthService>());
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
}
