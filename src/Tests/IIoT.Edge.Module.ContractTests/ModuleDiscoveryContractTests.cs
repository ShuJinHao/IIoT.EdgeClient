using IIoT.Edge.Module.DryRun;
using IIoT.Edge.Module.Injection;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Plugin.Shared.Modules;
using System.IO;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindInjectionStackingAndDryRun()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("DryRun", "Injection", "Stacking");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);

            Assert.Equal(
                ["DryRun", "Injection", "Stacking"],
                discovery.Modules.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateAllModules_ShouldInstantiateAllDiscoveredPluginsWithoutDuplicateIdentity()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("DryRun", "Injection", "Stacking");
        try
        {
            var modules = DirectoryModuleCatalog.CreateAllModules(DiscoverPlugins(pluginRoot).Modules);

            Assert.Equal(3, modules.Count);
            Assert.Equal(3, modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(3, modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(modules, x => x is InjectionModule);
            Assert.Contains(modules, x => x is DryRunModule);
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Stacking", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void RegisterAllDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("DryRun", "Injection", "Stacking");
        try
        {
            var modules = DirectoryModuleCatalog.CreateAllModules(DiscoverPlugins(pluginRoot).Modules);
            var services = new ServiceCollection();
            var viewRegistry = new ViewRegistry();
            var cellDataRegistry = new CellDataRegistry();
            var runtimeRegistry = new StationRuntimeRegistry();
            var integrationRegistry = new ProcessIntegrationRegistry();

            foreach (var module in modules)
            {
                module.Configure(new TestEdgeProcessModuleBuilder(
                    module.ModuleId,
                    module.ProcessType,
                    services,
                    new ModuleViewRegistry(viewRegistry, module.ModuleId),
                    cellDataRegistry,
                    runtimeRegistry,
                    integrationRegistry));
            }

            Assert.Equal(3, cellDataRegistry.GetRegistrations().Count);
            Assert.Equal(3, runtimeRegistry.GetRegistrations().Count);
            Assert.Equal(3, integrationRegistry.GetCloudUploaders().Count);
            Assert.NotNull(viewRegistry.GetViewRegistration("Injection.DataView"));
            Assert.NotNull(viewRegistry.GetViewRegistration("Stacking.DataView"));
            Assert.NotNull(viewRegistry.GetViewRegistration("DryRun.Dashboard"));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    private static ModuleCatalogDiscoveryResult DiscoverPlugins(string pluginRoot)
    {
        var discovery = DirectoryModuleCatalog.DiscoverModules(pluginRoot);
        Assert.Empty(discovery.Issues);
        return discovery;
    }
}
