using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Shell.Modules;
using Microsoft.Extensions.Configuration;
using System.Security;
using System.Text;
using Xunit;

namespace IIoT.Edge.Module.ConformanceTests;

public sealed class ShellModuleCatalogExternalPluginBehaviorTests
{
    [Fact]
    public void GetPluginRootPaths_WhenPluginRootsAreNotConfigured_ShouldUseLayoutPluginsRoot()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var catalog = CreateCatalog();
            var configuration = new ConfigurationBuilder().Build();

            var paths = catalog.GetPluginRootPaths(hostDirectory, configuration);

            var path = Assert.Single(paths);
            Assert.Equal(
                Path.Combine(tempDirectory, EdgeClientProgramDataPaths.PluginsDirectoryName),
                path);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void GetPluginRootPaths_WhenPluginRootsAreConfigured_ShouldResolveRelativeToHostDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            var customPluginRoot = Path.Combine(tempDirectory, "custom-plugins");
            Directory.CreateDirectory(hostDirectory);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Modules:PluginRoots:0"] = "../custom-plugins"
                })
                .Build();

            var paths = CreateCatalog().GetPluginRootPaths(hostDirectory, configuration);

            var path = Assert.Single(paths);
            Assert.Equal(customPluginRoot, path);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void GetPluginRootPaths_WhenVelopackCurrentHostUsesDefaultPluginsRoot_ShouldUseInstallRootPlugins()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "install", "current", "host");
            var pluginsRoot = Path.Combine(tempDirectory, "install", "plugins");
            Directory.CreateDirectory(hostDirectory);
            Directory.CreateDirectory(pluginsRoot);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Modules:PluginRoots:0"] = "../plugins"
                })
                .Build();

            var paths = CreateCatalog().GetPluginRootPaths(hostDirectory, configuration);

            var path = Assert.Single(paths);
            Assert.Equal(pluginsRoot, path);
            Assert.NotEqual(Path.Combine(tempDirectory, "install", "current", "plugins"), path);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void GetPluginRootPaths_WhenConfiguredRootIsInvalid_ShouldUseDefaultRootWithoutThrowing()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Modules:PluginRoots:0"] = "bad\0root"
                })
                .Build();

            var paths = CreateCatalog().GetPluginRootPaths(hostDirectory, configuration);

            Assert.Equal(
                Path.Combine(tempDirectory, EdgeClientProgramDataPaths.PluginsDirectoryName),
                Assert.Single(paths));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(SecurityException))]
    public void GetPluginRootPaths_WhenResolverThrowsApprovedPathException_ShouldUseDefaultRootExactlyOnce(
        Type exceptionType)
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Modules:PluginRoots:0"] = "configured-plugins"
                })
                .Build();
            var expected = (Exception)Activator.CreateInstance(exceptionType, "recoverable configured-root failure")!;
            var callCount = 0;
            var catalog = new ShellModuleCatalog(
                new DirectoryModuleCatalog(
                    new ModulePluginLoader(new ModulePluginAssemblyResolver()),
                    new ModulePluginCompatibilityPolicy()),
                (_, _) =>
                {
                    callCount++;
                    throw expected;
                });

            var paths = catalog.GetPluginRootPaths(hostDirectory, configuration);

            Assert.Equal(1, callCount);
            Assert.Equal(
                Path.Combine(tempDirectory, EdgeClientProgramDataPaths.PluginsDirectoryName),
                Assert.Single(paths));
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void GetPluginRootPaths_WhenResolverThrowsUnknownException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertConfiguredRootResolverExceptionPropagates(
            new InvalidOperationException("unexpected configured-root failure"));
    }

    [Fact]
    public void GetPluginRootPaths_WhenResolverThrowsOperationCanceledException_ShouldPropagateSameInstanceExactlyOnce()
    {
        AssertConfiguredRootResolverExceptionPropagates(
            new OperationCanceledException("configured-root canceled"));
    }

    private static void AssertConfiguredRootResolverExceptionPropagates(Exception expected)
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Modules:PluginRoots:0"] = "configured-plugins"
                })
                .Build();
            var callCount = 0;
            var catalog = new ShellModuleCatalog(
                new DirectoryModuleCatalog(
                    new ModulePluginLoader(new ModulePluginAssemblyResolver()),
                    new ModulePluginCompatibilityPolicy()),
                (_, _) =>
                {
                    callCount++;
                    throw expected;
                });

            var actual = Assert.Throws(expected.GetType(), () =>
                catalog.GetPluginRootPaths(hostDirectory, configuration));

            Assert.Same(expected, actual);
            Assert.Equal(1, callCount);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void DiscoverModules_WhenConfiguredRootsContainDuplicateModule_ShouldPreferLaterRoot()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var firstRoot = Path.Combine(tempDirectory, "plugins-a");
            var secondRoot = Path.Combine(tempDirectory, "plugins-b");
            WritePlugin(
                Path.Combine(firstRoot, "TestPluginAlpha"),
                "TestPluginAlpha",
                "TestProcessAlpha",
                "1.0.0",
                "First.dll");
            WritePlugin(
                Path.Combine(secondRoot, "TestPluginAlpha"),
                "TestPluginAlpha",
                "TestProcessAlpha",
                "2.0.0",
                "Second.dll");

            var catalog = CreateCatalog();
            var discovery = catalog.DiscoverModules([firstRoot, secondRoot]);

            var descriptor = Assert.Single(discovery.Modules);
            Assert.Empty(discovery.Issues);
            Assert.Equal("2.0.0", descriptor.Version);
            Assert.EndsWith(
                Path.Combine("plugins-b", "TestPluginAlpha", "plugin.json"),
                descriptor.ManifestPath,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private static ShellModuleCatalog CreateCatalog()
        => new(new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy()));

    private static void WritePlugin(
        string pluginDirectory,
        string moduleId,
        string processType,
        string version,
        string entryAssembly)
    {
        Directory.CreateDirectory(pluginDirectory);
        WriteText(Path.Combine(pluginDirectory, entryAssembly), string.Empty);
        WriteText(
            Path.Combine(pluginDirectory, "plugin.json"),
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "{{moduleId}}",
              "version": "{{version}}",
              "hostApiVersion": "{{ModulePluginHostRuntime.HostApiVersion}}",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "{{entryAssembly}}",
              "entryType": "{{moduleId}}.DependencyInjection",
              "supportedProcessType": "{{processType}}",
              "dependencies": []
            }
            """);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-shell-module-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
        catch
        {
        }
    }
}
