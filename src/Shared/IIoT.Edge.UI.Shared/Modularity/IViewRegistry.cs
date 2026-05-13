using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.UI.Shared.Modularity;

public interface IViewRegistry
{
    void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true);
    void RegisterRoute(
        string viewId,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, ViewModelBase> viewModelFactory,
        bool cacheView = true);
    void RegisterMenu(MenuInfo menuInfo);
    void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true);
    void RegisterAnchorable(
        AnchorableInfo info,
        Type viewType,
        Type viewModelType,
        Func<IServiceProvider, ViewModelBase> viewModelFactory,
        bool cacheView = true);
    ViewRegistration? GetViewRegistration(string viewId);
    IReadOnlyList<MenuInfo> GetAllMenus();
    IReadOnlyList<AnchorableInfo> GetAllAnchorables();
}
