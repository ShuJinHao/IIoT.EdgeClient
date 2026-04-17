using IIoT.Edge.Module.DryRun;
using IIoT.Edge.Module.Injection;
using IIoT.Edge.Module.Stacking;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleDiscoveryContractTests
{
    [Fact]
    public void DiscoverCompiledModules_ShouldFindInjectionStackingAndDryRun()
    {
        var descriptors = CompiledModuleCatalog.DiscoverCompiledModules();

        Assert.Equal(
            ["DryRun", "Injection", "Stacking"],
            descriptors.Select(x => x.ModuleId).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void CreateAllModules_ShouldInstantiateAllCompiledModulesWithoutDuplicateIdentity()
    {
        var modules = CompiledModuleCatalog.CreateAllModules();

        Assert.Equal(3, modules.Count);
        Assert.Equal(3, modules.Select(x => x.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, modules.Select(x => x.ProcessType).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(modules, x => x is InjectionModule);
        Assert.Contains(modules, x => x is StackingModule);
        Assert.Contains(modules, x => x is DryRunModule);
    }

    [Fact]
    public void RegisterAllDiscoveredModules_ShouldNotProduceViewOrRegistrationConflicts()
    {
        var modules = CompiledModuleCatalog.CreateAllModules();
        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var cellDataRegistry = new CellDataRegistry();
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();

        foreach (var module in modules)
        {
            module.RegisterServices(services);
            module.RegisterCellData(cellDataRegistry);
            module.RegisterRuntime(runtimeRegistry);
            module.RegisterIntegrations(integrationRegistry);
            module.RegisterViews(new ModuleViewRegistry(viewRegistry, module.ModuleId));
        }

        Assert.Equal(3, cellDataRegistry.GetRegistrations().Count);
        Assert.Equal(3, runtimeRegistry.GetRegistrations().Count);
        Assert.Equal(3, integrationRegistry.GetCloudUploaders().Count);
        Assert.NotNull(viewRegistry.GetViewRegistration("Injection.DataView"));
        Assert.NotNull(viewRegistry.GetViewRegistration("Stacking.PlaceholderDashboard"));
        Assert.NotNull(viewRegistry.GetViewRegistration("DryRun.Dashboard"));
    }
}
