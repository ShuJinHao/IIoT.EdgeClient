using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
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
    private readonly IModuleParamRegistry _moduleParamRegistry;

    public EdgeProcessModuleBuilder(
        string moduleId,
        string processType,
        IServiceCollection services,
        IConfiguration configuration,
        IViewRegistry viewRegistry,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IModuleParamRegistry moduleParamRegistry)
    {
        ModuleId = string.IsNullOrWhiteSpace(moduleId)
            ? throw new ArgumentException("ModuleId 不能为空。", nameof(moduleId))
            : moduleId;
        ProcessType = string.IsNullOrWhiteSpace(processType)
            ? throw new ArgumentException("ProcessType 不能为空。", nameof(processType))
            : processType;
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _viewRegistry = viewRegistry ?? throw new ArgumentNullException(nameof(viewRegistry));
        _cellDataRegistry = cellDataRegistry ?? throw new ArgumentNullException(nameof(cellDataRegistry));
        _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
        _integrationRegistry = integrationRegistry ?? throw new ArgumentNullException(nameof(integrationRegistry));
        _moduleParamRegistry = moduleParamRegistry ?? throw new ArgumentNullException(nameof(moduleParamRegistry));
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
                $"模块“{ModuleId}”的运行时工厂必须实现 {nameof(IStationRuntimeFactory)}。");
        }

        if (!string.Equals(factory.ModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"运行时工厂 ModuleId“{factory.ModuleId}”与当前模块“{ModuleId}”不一致。");
        }

        _runtimeRegistry.Register(factory);
    }

    public void RegisterCloudUploader(ProcessUploadMode uploadMode)
        => _integrationRegistry.RegisterCloudUploader(ProcessType, uploadMode);

    public void RegisterMesUploader(ProcessUploadMode uploadMode)
        => _integrationRegistry.RegisterMesUploader(ProcessType, uploadMode);

    public void RegisterPlcSignalProfile<TSignalKey, TProfile>()
        where TSignalKey : struct, Enum
        where TProfile : class, IModulePlcSignalProfile<TSignalKey>
    {
        Services.AddSingleton<TProfile>();
        Services.AddSingleton<IModulePlcSignalProfile<TSignalKey>>(serviceProvider =>
        {
            var profile = serviceProvider.GetRequiredService<TProfile>();
            EnsureModuleId(profile.ModuleId, typeof(TProfile).Name);
            return profile;
        });
    }

    public void RegisterStandardPlcSignalProfiles<TInteraction, TSingleRead, TContinuousRead, TSingleWrite, TContinuousWrite>()
        where TInteraction : struct, Enum
        where TSingleRead : struct, Enum
        where TContinuousRead : struct, Enum
        where TSingleWrite : struct, Enum
        where TContinuousWrite : struct, Enum
    {
        Services.AddSingleton<IModulePlcSignalProfile<TInteraction>>(
            _ => new EnumInteractionSignalProfile<TInteraction>(ModuleId));
        Services.AddSingleton<IModulePlcSignalProfile<TSingleRead>>(
            _ => new EnumReadSignalProfile<TSingleRead>(ModuleId, IoMappingOptionCatalog.CategorySingleRead));
        Services.AddSingleton<IModulePlcSignalProfile<TContinuousRead>>(
            _ => new EnumReadSignalProfile<TContinuousRead>(ModuleId, IoMappingOptionCatalog.CategoryContinuousRead));
        Services.AddSingleton<IModulePlcSignalProfile<TSingleWrite>>(
            _ => new EnumWriteSignalProfile<TSingleWrite>(ModuleId, IoMappingOptionCatalog.CategorySingleWrite));
        Services.AddSingleton<IModulePlcSignalProfile<TContinuousWrite>>(
            _ => new EnumWriteSignalProfile<TContinuousWrite>(ModuleId, IoMappingOptionCatalog.CategoryContinuousWrite));
    }

    public void RegisterHardwareProfile<TProvider>()
        where TProvider : class, IModuleHardwareProfileProvider
    {
        Services.AddSingleton<TProvider>();
        Services.AddSingleton<IModuleHardwareProfileProvider>(serviceProvider =>
        {
            var provider = serviceProvider.GetRequiredService<TProvider>();
            EnsureModuleId(provider.ModuleId, typeof(TProvider).Name);
            return provider;
        });
    }

    public void RegisterDevelopmentSample<TContributor>()
        where TContributor : class, IDevelopmentSampleContributor
    {
        Services.AddSingleton<TContributor>();
        Services.AddSingleton<IDevelopmentSampleContributor>(serviceProvider =>
        {
            var contributor = serviceProvider.GetRequiredService<TContributor>();
            EnsureModuleId(contributor.ModuleId, typeof(TContributor).Name);
            return contributor;
        });
    }

    public void RegisterParameters<TMes, TCloud, TBusiness>(
        IReadOnlyCollection<ModuleParamDefaultOverride>? defaultOverrides = null)
        where TMes : struct, Enum
        where TCloud : struct, Enum
        where TBusiness : struct, Enum
        => _moduleParamRegistry.Register(
            ModuleId,
            typeof(TMes),
            typeof(TCloud),
            typeof(TBusiness),
            defaultOverrides);

    private void EnsureModuleId(string registeredModuleId, string registrationName)
    {
        if (!string.Equals(registeredModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"模块注册【{registrationName}】的 ModuleId【{registeredModuleId}】与当前模块【{ModuleId}】不一致。");
        }
    }

    private static ViewModelBase ResolveViewModel(
        string viewId,
        Func<IServiceProvider, object> viewModelFactory,
        IServiceProvider serviceProvider)
    {
        var viewModel = viewModelFactory(serviceProvider);
        return viewModel as ViewModelBase
            ?? throw new InvalidOperationException(
                $"视图“{viewId}”的 ViewModel 工厂必须返回 {nameof(ViewModelBase)}。");
    }
}
