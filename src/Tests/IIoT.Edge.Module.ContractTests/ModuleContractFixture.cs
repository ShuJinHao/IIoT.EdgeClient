using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ModuleContractFixture
{
    public ModuleContractResult RegisterModule(IEdgeProcessModule module, IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(module);

        var services = new ServiceCollection();
        var viewRegistry = new AvaloniaViewRegistry();
        var cellDataRegistry = new CellDataRegistry(new CellDataTypeRegistry());
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        var moduleParamRegistry = new ModuleParamRegistry();
        configuration ??= new ConfigurationBuilder().Build();
        var builder = new TestEdgeProcessModuleBuilder(
            module.ModuleId,
            module.ProcessType,
            services,
            configuration,
            viewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry,
            moduleParamRegistry);

        module.Configure(builder);

        return new ModuleContractResult(
            services,
            viewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry,
            moduleParamRegistry);
    }
}

public sealed record ModuleContractResult(
    IServiceCollection Services,
    AvaloniaViewRegistry ViewRegistry,
    CellDataRegistry CellDataRegistry,
    StationRuntimeRegistry RuntimeRegistry,
    ProcessIntegrationRegistry IntegrationRegistry,
    ModuleParamRegistry ModuleParamRegistry);

internal sealed class TestEdgeProcessModuleBuilder(
    string moduleId,
    string processType,
    IServiceCollection services,
    IConfiguration configuration,
    IAvaloniaViewRegistry viewRegistry,
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry,
    IProcessIntegrationRegistry integrationRegistry,
    IModuleParamRegistry moduleParamRegistry) : IEdgeProcessModuleBuilder
{
    public string ModuleId { get; } = moduleId;

    public string ProcessType { get; } = processType;

    public IServiceCollection Services { get; } = services;

    public IConfiguration Configuration { get; } = configuration;

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        => viewRegistry.RegisterRoute(viewId, viewType, viewModelType, cacheView: cacheView);

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

    public void RegisterMenu(ModuleMenuDescriptor menuInfo)
        => viewRegistry.RegisterMenu(new AvaloniaMenuInfo
        {
            TitleResourceKey = menuInfo.TitleResourceKey,
            Title = menuInfo.Title,
            Icon = menuInfo.Icon,
            RequiredPermission = menuInfo.RequiredPermission,
            ViewId = menuInfo.ViewId,
            Order = menuInfo.Order
        });

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

    private static AvaloniaDockPaneInfo ToDockPaneInfo(ModulePanelDescriptor info)
        => new()
        {
            TitleResourceKey = info.TitleResourceKey,
            ViewId = info.ContentId,
            DockGroup = info.InitialPosition == ModulePanelPosition.Main ? "documents" : "tools",
            IsToolPane = info.InitialPosition != ModulePanelPosition.Main
        };

    private void RegisterPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        bool cacheView)
        => viewRegistry.RegisterDockPane(
            ToDockPaneInfo(info),
            viewType,
            viewModelType,
            cacheView: cacheView);

    private void RegisterPanel(
        ModulePanelDescriptor info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView)
        => viewRegistry.RegisterDockPane(
            ToDockPaneInfo(info),
            viewType,
            viewModelType,
            serviceProvider => ResolveViewModel(info.ContentId, viewModelFactory, serviceProvider),
            cacheView);

    public void RegisterCellData(Type cellDataType)
        => cellDataRegistry.Register(ProcessType, cellDataType);

    public void RegisterRuntimeFactory(object runtimeFactory)
        => runtimeRegistry.Register((IStationRuntimeFactory)runtimeFactory);

    public void RegisterCloudUploader(ProcessUploadMode uploadMode)
        => integrationRegistry.RegisterCloudUploader(ProcessType, uploadMode);

    public void RegisterMesUploader(MesUploadMode uploadMode)
        => integrationRegistry.RegisterMesUploader(ProcessType, uploadMode);

    public void RegisterParameters<TMes, TCloud, TBusiness>()
        where TMes : struct, Enum
        where TCloud : struct, Enum
        where TBusiness : struct, Enum
        => moduleParamRegistry.Register(ModuleId, typeof(TMes), typeof(TCloud), typeof(TBusiness));

    public void RegisterPlcSignalProfile<TSignalKey, TProfile>()
        where TSignalKey : struct, Enum
        where TProfile : class, IModulePlcSignalProfile<TSignalKey>
    {
        Services.AddSingleton<TProfile>();
        Services.AddSingleton<IModulePlcSignalProfile<TSignalKey>>(serviceProvider =>
            serviceProvider.GetRequiredService<TProfile>());
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
        => Services.AddSingleton<IModuleHardwareProfileProvider, TProvider>();

    public void RegisterDevelopmentSample<TContributor>()
        where TContributor : class, IDevelopmentSampleContributor
        => Services.AddSingleton<IDevelopmentSampleContributor, TContributor>();

    private static object ResolveViewModel(
        string viewId,
        Func<IServiceProvider, object> viewModelFactory,
        IServiceProvider serviceProvider)
    {
        var viewModel = viewModelFactory(serviceProvider);
        return viewModel
            ?? throw new InvalidOperationException($"View model factory for '{viewId}' returned null.");
    }
}
