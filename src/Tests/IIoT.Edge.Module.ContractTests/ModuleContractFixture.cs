namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleContractFixture
{
    public ModuleContractResult RegisterModule(IEdgeStationModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var moduleViewRegistry = new ModuleViewRegistry(viewRegistry, module.ModuleId);
        var cellDataRegistry = new CellDataRegistry();
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();

        module.RegisterServices(services);
        module.RegisterCellData(cellDataRegistry);
        module.RegisterRuntime(runtimeRegistry);
        module.RegisterIntegrations(integrationRegistry);
        module.RegisterViews(moduleViewRegistry);

        return new ModuleContractResult(
            services,
            viewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry);
    }
}

public sealed record ModuleContractResult(
    IServiceCollection Services,
    ViewRegistry ViewRegistry,
    CellDataRegistry CellDataRegistry,
    StationRuntimeRegistry RuntimeRegistry,
    ProcessIntegrationRegistry IntegrationRegistry);
