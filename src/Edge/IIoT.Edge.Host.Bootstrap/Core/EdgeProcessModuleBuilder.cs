using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Plugin.Shared.Modules;
using IIoT.Edge.UI.Shared.Modularity;
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
        _viewRegistry = viewRegistry ?? throw new ArgumentNullException(nameof(viewRegistry));
        _cellDataRegistry = cellDataRegistry ?? throw new ArgumentNullException(nameof(cellDataRegistry));
        _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
        _integrationRegistry = integrationRegistry ?? throw new ArgumentNullException(nameof(integrationRegistry));
    }

    public string ModuleId { get; }

    public string ProcessType { get; }

    public IServiceCollection Services { get; }

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        => _viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView);

    public void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, IIoT.Edge.UI.Shared.PluginSystem.ViewModelBase> viewModelFactory,
        bool cacheView = true)
        => _viewRegistry.RegisterRoute(viewId, viewType, viewModelType, viewModelFactory, cacheView);

    public void RegisterMenu(EdgeMenuInfo menuInfo)
    {
        ArgumentNullException.ThrowIfNull(menuInfo);
        _viewRegistry.RegisterMenu(new MenuInfo
        {
            Title = menuInfo.Title,
            ViewId = menuInfo.ViewId,
            Icon = menuInfo.Icon,
            Order = menuInfo.Order,
            RequiredPermission = menuInfo.RequiredPermission
        });
    }

    public void RegisterAnchorable(
        EdgeAnchorableInfo info,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
    {
        ArgumentNullException.ThrowIfNull(info);
        _viewRegistry.RegisterAnchorable(
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
    }

    public void RegisterAnchorable(
        EdgeAnchorableInfo info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, IIoT.Edge.UI.Shared.PluginSystem.ViewModelBase> viewModelFactory,
        bool cacheView = true)
    {
        ArgumentNullException.ThrowIfNull(info);
        _viewRegistry.RegisterAnchorable(
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
            viewModelFactory,
            cacheView);
    }

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
}
