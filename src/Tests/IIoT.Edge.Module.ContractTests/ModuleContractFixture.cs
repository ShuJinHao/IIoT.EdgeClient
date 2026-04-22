using IIoT.Edge.Plugin.Shared.Modules;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleContractFixture
{
    public ModuleContractResult RegisterModule(IEdgeProcessModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var moduleViewRegistry = new ModuleViewRegistry(viewRegistry, module.ModuleId);
        var cellDataRegistry = new CellDataRegistry();
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        var builder = new TestEdgeProcessModuleBuilder(
            module.ModuleId,
            module.ProcessType,
            services,
            moduleViewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry);

        module.Configure(builder);

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

internal sealed class TestEdgeProcessModuleBuilder(
    string moduleId,
    string processType,
    IServiceCollection services,
    IViewRegistry viewRegistry,
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry,
    IProcessIntegrationRegistry integrationRegistry) : IEdgeProcessModuleBuilder
{
    public string ModuleId { get; } = moduleId;

    public string ProcessType { get; } = processType;

    public IServiceCollection Services { get; } = services;

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        => viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView);

    public void RegisterMenu(EdgeMenuInfo menuInfo)
        => viewRegistry.RegisterMenu(new MenuInfo
        {
            Title = menuInfo.Title,
            ViewId = menuInfo.ViewId,
            Icon = menuInfo.Icon,
            Order = menuInfo.Order,
            RequiredPermission = menuInfo.RequiredPermission
        });

    public void RegisterAnchorable(
        EdgeAnchorableInfo info,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
        => viewRegistry.RegisterAnchorable(
            new AnchorableInfo
            {
                Title = info.Title,
                ContentId = info.ContentId,
                InitialPosition = info.InitialPosition switch
                {
                    EdgeAnchorablePosition.Left => AnchorablePosition.Left,
                    EdgeAnchorablePosition.Right => AnchorablePosition.Right,
                    EdgeAnchorablePosition.Bottom => AnchorablePosition.Bottom,
                    _ => AnchorablePosition.Main
                },
                IsVisible = info.IsVisible
            },
            viewType,
            viewModelType,
            cacheView);

    public void RegisterCellData(Type cellDataType)
        => cellDataRegistry.Register(ProcessType, cellDataType);

    public void RegisterRuntimeFactory(object runtimeFactory)
        => runtimeRegistry.Register((IStationRuntimeFactory)runtimeFactory);

    public void RegisterCloudUploader(PluginCloudUploadMode uploadMode)
        => integrationRegistry.RegisterCloudUploader(
            ProcessType,
            uploadMode == PluginCloudUploadMode.Batch ? ProcessUploadMode.Batch : ProcessUploadMode.Single);

    public void RegisterMesUploader(PluginMesUploadMode uploadMode)
        => integrationRegistry.RegisterMesUploader(ProcessType, MesUploadMode.Single);
}
