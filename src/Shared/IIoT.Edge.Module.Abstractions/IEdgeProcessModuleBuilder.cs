using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Plugin.Shared.Modules;

public interface IEdgeProcessModuleBuilder
{
    string ModuleId { get; }

    string ProcessType { get; }

    IServiceCollection Services { get; }

    void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true);

    void RegisterMenu(EdgeMenuInfo menuInfo);

    void RegisterAnchorable(
        EdgeAnchorableInfo info,
        Type viewType,
        Type viewModelType,
        bool cacheView = true);

    void RegisterCellData(Type cellDataType);

    void RegisterRuntimeFactory(object runtimeFactory);

    void RegisterCloudUploader(PluginCloudUploadMode uploadMode);

    void RegisterMesUploader(PluginMesUploadMode uploadMode);
}
