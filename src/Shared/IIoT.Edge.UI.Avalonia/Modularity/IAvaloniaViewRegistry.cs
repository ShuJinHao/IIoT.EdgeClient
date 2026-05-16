namespace IIoT.Edge.UI.Avalonia.Modularity;

public interface IAvaloniaViewRegistry
{
    void RegisterRoute(string viewId, Type viewType, Type viewModelType, Func<IServiceProvider, object>? viewModelFactory = null, bool cacheView = true);

    void RegisterMenu(AvaloniaMenuInfo menuInfo);

    void RegisterDockPane(AvaloniaDockPaneInfo info, Type viewType, Type viewModelType, Func<IServiceProvider, object>? viewModelFactory = null, bool cacheView = true);

    AvaloniaViewRegistration? GetViewRegistration(string viewId);

    IReadOnlyList<AvaloniaViewRegistration> GetAllViewRegistrations();

    IReadOnlyList<AvaloniaMenuInfo> GetAllMenus();

    IReadOnlyList<AvaloniaDockPaneInfo> GetAllDockPanes();
}
