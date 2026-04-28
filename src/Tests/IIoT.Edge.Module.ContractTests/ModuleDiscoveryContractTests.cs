using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void DiscoverDirectoryPlugins_ShouldFindProductModules()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization", "Injection", "Stacking");
        try
        {
            var discovery = DiscoverPlugins(pluginRoot);

            Assert.Equal(
                ["Homogenization", "Injection", "Stacking"],
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
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization", "Injection", "Stacking");
        try
        {
            var modules = DirectoryModuleCatalog.CreateAllModules(DiscoverPlugins(pluginRoot).Modules);

            Assert.Equal(3, modules.Count);
            Assert.Equal(3, modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(3, modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Injection", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Homogenization", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(modules, x => string.Equals(x.ModuleId, "Stacking", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            ContractTestPathHelper.DeleteDirectory(pluginRoot);
        }
    }

    [Fact]
    public void CreateModule_WhenEntryTypeDoesNotImplementProcessModule_ShouldRejectWithClearContractMessage()
    {
        var assemblyPath = typeof(NonModuleEntry).Assembly.Location;
        var descriptor = new ModulePluginDescriptor(
            "BadModule",
            "BadProcess",
            "错误模块",
            "1.0.0",
            ModulePluginHostRuntime.HostApiVersion,
            "1.0.0",
            "99.0.0",
            [],
            Path.GetFileNameWithoutExtension(assemblyPath),
            typeof(NonModuleEntry).FullName!,
            Path.GetDirectoryName(assemblyPath)!,
            Path.Combine(Path.GetDirectoryName(assemblyPath)!, "plugin.json"),
            assemblyPath);

        var ex = Assert.Throws<InvalidOperationException>(() => descriptor.CreateModule());

        Assert.Contains(nameof(IEdgeProcessModule), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IEdgeStationModule", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterAllDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts()
    {
        var pluginRoot = ContractTestPathHelper.CreatePluginRuntimeRoot("Homogenization", "Injection", "Stacking");
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
                    new ConfigurationBuilder().Build(),
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
            Assert.NotNull(viewRegistry.GetViewRegistration("Homogenization.DataView"));
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

    private sealed class NonModuleEntry
    {
    }
}
