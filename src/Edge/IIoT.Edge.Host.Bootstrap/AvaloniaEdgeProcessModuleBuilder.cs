using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Host.Bootstrap;

internal sealed class AvaloniaEdgeProcessModuleBuilder : IEdgeProcessModuleBuilder
{
    private readonly IAvaloniaViewRegistry _viewRegistry;
    private readonly ICellDataRegistry _cellDataRegistry;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IProcessIntegrationRegistry _integrationRegistry;
    private readonly IModuleParamRegistry _moduleParamRegistry;
    private readonly string _requiredPrefix;

    public AvaloniaEdgeProcessModuleBuilder(
        string moduleId,
        string processType,
        IServiceCollection services,
        IConfiguration configuration,
        IAvaloniaViewRegistry viewRegistry,
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
        _requiredPrefix = $"{ModuleId}.";
    }

    public string ModuleId { get; }

    public string ProcessType { get; }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
    {
        EnsureModulePrefix(viewId, nameof(viewId));
        _viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView: cacheView);
    }

    public void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
    {
        EnsureModulePrefix(viewId, nameof(viewId));
        ArgumentNullException.ThrowIfNull(viewModelFactory);
        _viewRegistry.RegisterRoute(viewId, viewType, viewModelType, viewModelFactory, cacheView);
    }

    public void RegisterMenu(ModuleMenuDescriptor menuInfo)
    {
        ArgumentNullException.ThrowIfNull(menuInfo);
        EnsureModulePrefix(menuInfo.ViewId, $"{nameof(menuInfo)}.{nameof(menuInfo.ViewId)}");
        _viewRegistry.RegisterMenu(new AvaloniaMenuInfo
        {
            ViewId = menuInfo.ViewId,
            Title = menuInfo.Title,
            TitleResourceKey = menuInfo.TitleResourceKey,
            Icon = menuInfo.Icon,
            Order = menuInfo.Order,
            RequiredPermission = menuInfo.RequiredPermission
        });
    }

    public void RegisterDocumentPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
        => RegisterPanel(descriptor, viewType, viewModelType, viewModelFactory: null, cacheView, isToolPane: false);

    public void RegisterDocumentPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
        => RegisterPanel(descriptor, viewType, viewModelType, viewModelFactory, cacheView, isToolPane: false);

    public void RegisterToolPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        bool cacheView = true)
        => RegisterPanel(descriptor, viewType, viewModelType, viewModelFactory: null, cacheView, isToolPane: true);

    public void RegisterToolPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true)
        => RegisterPanel(descriptor, viewType, viewModelType, viewModelFactory, cacheView, isToolPane: true);

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

    public void RegisterMesUploader(MesUploadMode uploadMode)
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

    public void RegisterParameters<TMes, TCloud, TBusiness>()
        where TMes : struct, Enum
        where TCloud : struct, Enum
        where TBusiness : struct, Enum
        => _moduleParamRegistry.Register(ModuleId, typeof(TMes), typeof(TCloud), typeof(TBusiness));

    private void RegisterPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object>? viewModelFactory,
        bool cacheView,
        bool isToolPane)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        EnsureModulePrefix(descriptor.ContentId, $"{nameof(descriptor)}.{nameof(descriptor.ContentId)}");
        _viewRegistry.RegisterDockPane(
            new AvaloniaDockPaneInfo
            {
                ViewId = descriptor.ContentId,
                TitleResourceKey = descriptor.TitleResourceKey,
                DockGroup = descriptor.InitialPosition == ModulePanelPosition.Main ? "documents" : "tools",
                IsToolPane = isToolPane || descriptor.InitialPosition != ModulePanelPosition.Main
            },
            viewType,
            viewModelType,
            viewModelFactory,
            cacheView);
    }

    private void EnsureModuleId(string registeredModuleId, string registrationName)
    {
        if (!string.Equals(registeredModuleId, ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"模块注册“{registrationName}”的 ModuleId“{registeredModuleId}”与当前模块“{ModuleId}”不一致。");
        }
    }

    private void EnsureModulePrefix(string viewId, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(viewId)
            || !viewId.StartsWith(_requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"模块“{ModuleId}”只能注册前缀为“{_requiredPrefix}”的视图。参数 {argumentName} 的当前值为“{viewId}”。");
        }
    }
}
