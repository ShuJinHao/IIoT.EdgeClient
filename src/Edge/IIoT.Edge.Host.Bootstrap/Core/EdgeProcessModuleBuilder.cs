using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell.Core;

internal sealed class EdgeProcessModuleBuilder : IEdgeProcessModuleBuilder
{
    private readonly IViewRegistry _viewRegistry;
    private readonly ICellDataRegistry _cellDataRegistry;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IProcessIntegrationRegistry _integrationRegistry;

    public EdgeProcessModuleBuilder(
        string moduleId,
        string processType,
        IServiceCollection services,
        IConfiguration configuration,
        IViewRegistry viewRegistry,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry)
    {
        ModuleId = string.IsNullOrWhiteSpace(moduleId)
            ? throw new ArgumentException("ModuleId cannot be empty.", nameof(moduleId))
            : moduleId;
        ProcessType = string.IsNullOrWhiteSpace(processType)
            ? throw new ArgumentException("ProcessType cannot be empty.", nameof(processType))
            : processType;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _viewRegistry = viewRegistry ?? throw new ArgumentNullException(nameof(viewRegistry));
        _cellDataRegistry = cellDataRegistry ?? throw new ArgumentNullException(nameof(cellDataRegistry));
        _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
        _integrationRegistry = integrationRegistry ?? throw new ArgumentNullException(nameof(integrationRegistry));
    }

    public string ModuleId { get; }

    public string ProcessType { get; }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        => _viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView);

    public void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
    {
        ArgumentNullException.ThrowIfNull(viewModelFactory);
        _viewRegistry.RegisterRoute(
            viewId,
            viewType,
            viewModelType,
            serviceProvider => ResolveViewModel(viewId, viewModelFactory, serviceProvider),
            cacheView);
    }

    public void RegisterMenu(ModuleMenuDescriptor menuInfo)
    {
        ArgumentNullException.ThrowIfNull(menuInfo);
        _viewRegistry.RegisterMenu(new MenuInfo
        {
            Title = menuInfo.Title,
            TitleResourceKey = menuInfo.TitleResourceKey,
            ViewId = menuInfo.ViewId,
            Icon = menuInfo.Icon,
            Order = menuInfo.Order,
            RequiredPermission = menuInfo.RequiredPermission
        });
    }

    public void RegisterDocumentPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
        => RegisterPanel(info, viewType, viewModelType, cacheView);

    public void RegisterDocumentPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
        => RegisterPanel(info, viewType, viewModelType, viewModelFactory, cacheView);

    public void RegisterToolPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
        => RegisterPanel(info, viewType, viewModelType, cacheView);

    public void RegisterToolPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
        => RegisterPanel(info, viewType, viewModelType, viewModelFactory, cacheView);

    private void RegisterPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        bool cacheView)
    {
        ArgumentNullException.ThrowIfNull(info);
        _viewRegistry.RegisterAnchorable(
            ToAnchorableInfo(info),
            viewType,
            viewModelType,
            cacheView);
    }

    private void RegisterPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(viewModelFactory);
        _viewRegistry.RegisterAnchorable(
            ToAnchorableInfo(info),
            viewType,
            viewModelType,
            serviceProvider => ResolveViewModel(info.ContentId, viewModelFactory, serviceProvider),
            cacheView);
    }

    private static AnchorableInfo ToAnchorableInfo(ModulePanelDescriptor info)
        => new()
        {
            Title = info.Title,
            TitleResourceKey = info.TitleResourceKey,
            ContentId = info.ContentId,
            InitialPosition = info.InitialPosition switch
            {
                ModulePanelPosition.Left => AnchorablePosition.Left,
                ModulePanelPosition.Right => AnchorablePosition.Right,
                ModulePanelPosition.Bottom => AnchorablePosition.Bottom,
                _ => AnchorablePosition.Main
            },
            IsVisible = info.IsVisible
        };

    public void RegisterCellData(Type cellDataType)
        => _cellDataRegistry.Register(ProcessType, cellDataType);

    public void RegisterRuntimeFactory(object runtimeFactory)
    {
        if (runtimeFactory is not IStationRuntimeFactory factory)
        {
            throw new InvalidOperationException(
                $"Runtime factory for module '{ModuleId}' must implement {nameof(IStationRuntimeFactory)}.");
        }

        if (!string.Equals(factory.ModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Runtime factory module id '{factory.ModuleId}' does not match builder module id '{ModuleId}'.");
        }

        _runtimeRegistry.Register(factory);
    }

    public void RegisterCloudUploader(PluginCloudUploadMode uploadMode)
        => _integrationRegistry.RegisterCloudUploader(
            ProcessType,
            uploadMode == PluginCloudUploadMode.Batch
                ? ProcessUploadMode.Batch
                : ProcessUploadMode.Single);

    public void RegisterMesUploader(PluginMesUploadMode uploadMode)
        => _integrationRegistry.RegisterMesUploader(
            ProcessType,
            uploadMode switch
            {
                _ => MesUploadMode.Single
            });

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
