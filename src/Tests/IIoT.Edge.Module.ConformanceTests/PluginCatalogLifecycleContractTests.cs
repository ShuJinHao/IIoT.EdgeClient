using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Module.ConformanceTests;

public sealed class PluginCatalogLifecycleContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiscoverModules_WhenEntryAssemblyEscapesStagedDirectory_ShouldRejectManifest(bool useAbsolutePath)
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            var outsideAssembly = Path.Combine(pluginRoot, "outside-test-plugin.dll");
            File.Copy(
                Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll"),
                outsideAssembly,
                overwrite: true);
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["entryAssembly"] = useAbsolutePath
                ? outsideAssembly
                : "../outside-test-plugin.dll";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", Assert.Single(discovery.Issues).Code);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenEntryAssemblySymlinkEscapesStagedDirectory_ShouldRejectManifest()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var entryAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var outsideAssemblyPath = Path.Combine(pluginRoot, "outside-test-plugin.dll");
            File.Copy(entryAssemblyPath, outsideAssemblyPath, overwrite: true);
            File.Delete(entryAssemblyPath);
            File.CreateSymbolicLink(entryAssemblyPath, outsideAssemblyPath);

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("staged", issue.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenPluginDirectorySymlinkEscapesPluginRoot_ShouldRejectManifest()
    {
        var outsideRoot = CreatePluginRoot("TestPlugin");
        var pluginRoot = ContractTestPathHelper.CreateTempDirectory("edge-plugin-root-boundary-tests");
        try
        {
            Directory.CreateSymbolicLink(
                Path.Combine(pluginRoot, "TestPlugin"),
                Path.Combine(outsideRoot, "TestPlugin"));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("真实路径", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
            ContractTestPathHelper.DeleteDirectory(outsideRoot);
        }
    }

    [Fact]
    public void DiscoverModules_WhenManifestSymlinkEscapesStagedDirectory_ShouldRejectManifest()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            var outsideManifestPath = Path.Combine(pluginRoot, "outside-plugin.json");
            File.Copy(manifestPath, outsideManifestPath, overwrite: true);
            File.Delete(manifestPath);
            File.CreateSymbolicLink(manifestPath, outsideManifestPath);

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
            Assert.Contains("staged", issue.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void AssemblyResolver_WhenDependencySymlinkEscapesStagedDirectory_ShouldRejectPlugin()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var pluginDirectory = Path.Combine(pluginRoot, "TestPlugin");
            var entryAssemblyPath = Path.Combine(pluginDirectory, "IIoT.Edge.TestPlugin.dll");
            var outsideDependencyPath = Path.Combine(pluginRoot, "outside-dependency.dll");
            File.Copy(entryAssemblyPath, outsideDependencyPath, overwrite: true);
            File.CreateSymbolicLink(
                Path.Combine(pluginDirectory, "EscapedDependency.dll"),
                outsideDependencyPath);
            using var resolver = new ModulePluginAssemblyResolver();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                resolver.LoadAssembly(entryAssemblyPath, pluginDirectory));

            Assert.Contains("依赖程序集", exception.Message, StringComparison.Ordinal);
            Assert.Contains("staged", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void HostRuntimeVersion_ShouldBeParseableAndDerivedFromHostAssemblyVersion()
    {
        var assemblyVersion = typeof(ModulePluginHostRuntime).Assembly.GetName().Version;

        Assert.True(ModulePluginHostRuntime.TryParseVersion(ModulePluginHostRuntime.HostVersion, out var hostVersion));
        Assert.NotNull(assemblyVersion);
        Assert.Equal(assemblyVersion!.Major, hostVersion.Major);
        Assert.Equal(assemblyVersion.Minor, hostVersion.Minor);
        Assert.Equal(Math.Max(assemblyVersion.Build, 0), hostVersion.Build);
        Assert.Equal("1.0.0", ModulePluginHostRuntime.HostApiVersion);
    }

    [Fact]
    public void DiscoverModules_WhenManifestMissesHostApiVersion_ShouldReportManifestInvalid()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest.Remove("hostApiVersion");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = CreateModuleCatalog().DiscoverModules(pluginRoot);

            Assert.Empty(discovery.Modules);
            var issue = Assert.Single(discovery.Issues);
            Assert.Equal("PLUGIN_MANIFEST_INVALID", issue.Code);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenHostApiVersionDoesNotMatch_ShouldReportCompatibilityIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["hostApiVersion"] = "99.0.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenHostVersionIsOutsideSupportedRange_ShouldReportCompatibilityIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["maxHostVersion"] = "0.9.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateEnabledModules_WhenDependencyIsNotEnabled_ShouldReportDependencyIssue()
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["dependencies"] = new JsonArray("MissingProcess");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "TestPlugin"
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Contains(activation.Issues, issue => string.Equals(issue.Code, "PLUGIN_DEPENDENCY_MISSING", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(activation.Modules);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Theory]
    [InlineData("moduleId", "ManifestAlias", "ModuleId")]
    [InlineData("supportedProcessType", "ManifestProcessAlias", "ProcessType")]
    public void CreateEnabledModules_WhenManifestIdentityDiffersFromRuntime_ShouldRejectActivation(
        string manifestProperty,
        string manifestValue,
        string runtimeProperty)
    {
        var pluginRoot = CreatePluginRoot("TestPlugin");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "TestPlugin", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest[manifestProperty] = manifestValue;
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));
            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            Assert.Empty(discovery.Issues);
            var descriptor = Assert.Single(discovery.Modules);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = descriptor.ModuleId
                    })
                    .Build(),
                "Modules",
                discovery.Modules);

            Assert.Empty(activation.Modules);
            var issue = Assert.Single(activation.Issues, issue => issue.Code == "PLUGIN_LOAD_FAILED");
            Assert.Contains(runtimeProperty, issue.Message, StringComparison.Ordinal);
            Assert.Contains("不一致", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    private static string CreatePluginRoot(params string[] moduleIds)
    {
        return ContractTestPathHelper.CreatePluginRuntimeRoot(moduleIds);
    }

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy());
}
