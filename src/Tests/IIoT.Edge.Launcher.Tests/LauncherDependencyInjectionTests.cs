using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherDependencyInjectionTests
{
    [Fact]
    public void AddLauncherServices_ShouldResolveLauncherRuntimeServices()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var services = new ServiceCollection()
                .AddLauncherServices(tempDirectory);

            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            Assert.IsType<LauncherAccountCatalogInitializer>(
                provider.GetRequiredService<ILauncherAccountCatalogInitializer>());
            Assert.IsType<LauncherAccountCatalog>(
                provider.GetRequiredService<ILauncherAccountCatalog>());
            Assert.IsType<LauncherProfileCatalog>(
                provider.GetRequiredService<ILauncherProfileCatalog>());
            Assert.IsType<LocalLauncherAuthService>(
                provider.GetRequiredService<ILocalLauncherAuthService>());
            Assert.IsType<ProcessStarter>(
                provider.GetRequiredService<IProcessStarter>());
            Assert.IsType<ShellLaunchService>(
                provider.GetRequiredService<IShellLaunchService>());
            Assert.NotNull(provider.GetRequiredService<LauncherMainViewModel>());
            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == typeof(MainWindow) &&
                descriptor.Lifetime == ServiceLifetime.Singleton);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void EnsureCatalogExists_WhenAccountCatalogMissing_ShouldCopySampleCatalog()
    {
        var tempDirectory = CreateTempDirectory();
        var samplePath = LauncherAccountCatalog.GetCatalogPath(
            tempDirectory,
            LauncherAccountCatalog.SampleCatalogFileName);
        var accountsPath = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
        const string sampleJson = "[]";
        try
        {
            File.WriteAllText(samplePath, sampleJson);
            var initializer = new LauncherAccountCatalogInitializer(tempDirectory);

            initializer.EnsureCatalogExists();

            Assert.True(File.Exists(accountsPath));
            Assert.Equal(sampleJson, File.ReadAllText(accountsPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void EnsureCatalogExists_WhenAccountCatalogExists_ShouldNotOverwriteCatalog()
    {
        var tempDirectory = CreateTempDirectory();
        var samplePath = LauncherAccountCatalog.GetCatalogPath(
            tempDirectory,
            LauncherAccountCatalog.SampleCatalogFileName);
        var accountsPath = LauncherAccountCatalog.GetCatalogPath(tempDirectory);
        try
        {
            File.WriteAllText(samplePath, "[]");
            File.WriteAllText(accountsPath, "[{\"userName\":\"edge-admin\"}]");
            var initializer = new LauncherAccountCatalogInitializer(tempDirectory);

            initializer.EnsureCatalogExists();

            Assert.Equal("[{\"userName\":\"edge-admin\"}]", File.ReadAllText(accountsPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-di-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }
}
