using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.Shell.Modules;
using System.Text;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ShellModuleCatalogExternalPluginBehaviorTests
{
    [Fact]
    public void GetPluginRootPaths_WhenMachineProfileIsProvided_ShouldIncludeProfileScopedExternalPluginRoot()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
        try
        {
            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var catalog = CreateCatalog();

                var paths = catalog.GetPluginRootPaths(tempDirectory, "HomogenizationLine");

                Assert.Equal(Path.Combine(tempDirectory, ShellModuleCatalog.PluginDirectoryName), paths[0]);
                Assert.Equal(
                    EdgeClientProgramDataPaths.ResolveProfilePluginRootPath("HomogenizationLine", tempDirectory),
                    paths[1]);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void DiscoverModules_WhenExternalProfilePluginMatchesBuiltInModule_ShouldPreferExternalPlugin()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRootOverride = Path.Combine(tempDirectory, "data-root");
        try
        {
            WritePlugin(
                Path.Combine(tempDirectory, ShellModuleCatalog.PluginDirectoryName, "Homogenization"),
                "Homogenization",
                "Homogenization",
                "1.0.0",
                "BuiltIn.dll");

            EdgeEnvironmentTestScope.WithDataRootOverride(dataRootOverride, () =>
            {
                var externalCurrentDirectory = EdgeClientProgramDataPaths.ResolveProfilePluginCurrentDirectory(
                    "HomogenizationLine",
                    "Homogenization",
                    tempDirectory);
                WritePlugin(
                    externalCurrentDirectory,
                    "Homogenization",
                    "Homogenization",
                    "2.0.0",
                    "External.dll");

                var catalog = CreateCatalog();
                var discovery = catalog.DiscoverModules(catalog.GetPluginRootPaths(tempDirectory, "HomogenizationLine"));

                var descriptor = Assert.Single(discovery.Modules);
                Assert.Empty(discovery.Issues);
                Assert.Equal("2.0.0", descriptor.Version);
                Assert.EndsWith(
                    Path.Combine("Homogenization", "current", "plugin.json"),
                    descriptor.ManifestPath,
                    StringComparison.Ordinal);
            });
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
