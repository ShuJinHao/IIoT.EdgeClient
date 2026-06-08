using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Module.ContractTests;

public sealed class PluginCatalogLifecycleContractTests
{
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
        var pluginRoot = CreatePluginRoot("Homogenization");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Homogenization", "plugin.json");
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
        var pluginRoot = CreatePluginRoot("Homogenization");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Homogenization", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["hostApiVersion"] = "99.0.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "Homogenization"
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
        var pluginRoot = CreatePluginRoot("Homogenization");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Homogenization", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["maxHostVersion"] = "0.9.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "Homogenization"
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
        var pluginRoot = CreatePluginRoot("Homogenization");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Homogenization", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["dependencies"] = new JsonArray("MissingProcess");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var catalog = CreateModuleCatalog();
            var discovery = catalog.DiscoverModules(pluginRoot);
            var activation = catalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "Homogenization"
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

    private static string CreatePluginRoot(params string[] moduleIds)
    {
        return ContractTestPathHelper.CreatePluginRuntimeRoot(moduleIds);
    }

    private static IModuleCatalog CreateModuleCatalog()
        => new DirectoryModuleCatalog(
            new ModulePluginLoader(new ModulePluginAssemblyResolver()),
            new ModulePluginCompatibilityPolicy());
}
