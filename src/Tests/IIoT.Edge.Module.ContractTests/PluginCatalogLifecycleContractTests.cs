using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Module.ContractTests;

public sealed class PluginCatalogLifecycleContractTests
{
    [Fact]
    public void DiscoverModules_WhenManifestMissesHostApiVersion_ShouldReportManifestInvalid()
    {
        var pluginRoot = CreatePluginRoot("Injection");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Injection", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest.Remove("hostApiVersion");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = DirectoryModuleCatalog.DiscoverModules(pluginRoot);

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
    public void CreateEnabledModules_WhenHostVersionIsOutsideSupportedRange_ShouldReportCompatibilityIssue()
    {
        var pluginRoot = CreatePluginRoot("Injection");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Injection", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["maxHostVersion"] = "0.9.0";
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = DirectoryModuleCatalog.DiscoverModules(pluginRoot);
            var activation = DirectoryModuleCatalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "Injection"
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
        var pluginRoot = CreatePluginRoot("Injection", "Stacking");
        try
        {
            var manifestPath = Path.Combine(pluginRoot, "Stacking", "plugin.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            manifest["dependencies"] = new JsonArray("Injection");
            File.WriteAllText(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }));

            var discovery = DirectoryModuleCatalog.DiscoverModules(pluginRoot);
            var activation = DirectoryModuleCatalog.CreateEnabledModules(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Modules:Enabled:0"] = "Stacking"
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
}
