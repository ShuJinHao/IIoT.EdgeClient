using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleContractFixture
{
    public ModuleContractResult RegisterModule(IEdgeProcessModule module, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        var services = new ServiceCollection();
        var viewRegistry = new ViewRegistry();
        var moduleViewRegistry = new ModuleViewRegistry(viewRegistry, module.ModuleId);
        var cellDataRegistry = new CellDataRegistry();
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        configuration ??= new ConfigurationBuilder().Build();
        var builder = new TestEdgeProcessModuleBuilder(
            module.ModuleId,
            module.ProcessType,
            services,
            configuration,
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
    IConfiguration configuration,
    IViewRegistry viewRegistry,
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry,
    IProcessIntegrationRegistry integrationRegistry) : IEdgeProcessModuleBuilder
{
    public string ModuleId { get; } = moduleId;

    public string ProcessType { get; } = processType;

    public IServiceCollection Services { get; } = services;

    public IConfiguration Configuration { get; } = configuration;

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        => viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView);

    public void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
        => viewRegistry.RegisterRoute(
            viewId,
            viewType,
            viewModelType,
            serviceProvider => ResolveViewModel(viewId, viewModelFactory, serviceProvider),
            cacheView);

    public void RegisterMenu(EdgeMenuInfo menuInfo)
        => viewRegistry.RegisterMenu(new MenuInfo
        {
            Title = menuInfo.Title,
            TitleResourceKey = menuInfo.TitleResourceKey,
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

    public void RegisterAnchorable(
        EdgeAnchorableInfo info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
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
            serviceProvider => ResolveViewModel(info.ContentId, viewModelFactory, serviceProvider),
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

    private static ViewModelBase ResolveViewModel(
        string viewId,
        Func<IServiceProvider, object> viewModelFactory,
        IServiceProvider serviceProvider)
    {
        var viewModel = viewModelFactory(serviceProvider);
        return viewModel as ViewModelBase
            ?? throw new InvalidOperationException(
                $"View model factory for '{viewId}' must return {nameof(ViewModelBase)}.");
    }
}
