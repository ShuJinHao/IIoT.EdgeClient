using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IEdgeProcessModuleBuilder
{
    string ModuleId { get; }

    string ProcessType { get; }

    IServiceCollection Services { get; }

    IConfiguration Configuration { get; }

    void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true);

    void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true);

    void RegisterMenu(ModuleMenuDescriptor menuInfo);

    void RegisterDocumentPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        bool cacheView = true);

    void RegisterDocumentPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true);

    void RegisterToolPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        bool cacheView = true);

    void RegisterToolPanel(
        ModulePanelDescriptor descriptor,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, object> viewModelFactory,
        bool cacheView = true);

    void RegisterCellData(Type cellDataType);

    void RegisterRuntimeFactory(object runtimeFactory);

    void RegisterCloudUploader(ProcessUploadMode uploadMode);

    void RegisterMesUploader(MesUploadMode uploadMode);
}
