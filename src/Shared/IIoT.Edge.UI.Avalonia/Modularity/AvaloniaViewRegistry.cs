namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaViewRegistry : IAvaloniaViewRegistry
{
    private readonly Dictionary<string, AvaloniaViewRegistration> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AvaloniaMenuInfo> _menus = [];
    private readonly List<AvaloniaDockPaneInfo> _dockPanes = [];

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, Func<IServiceProvider, object>? viewModelFactory = null, bool cacheView = true)
    {
        _views[viewId] = new AvaloniaViewRegistration
        {
            ViewId = viewId,
            ViewType = viewType,
            ViewModelType = viewModelType,
            ViewModelFactory = viewModelFactory,
            CacheView = cacheView
        };
    }

    public void RegisterMenu(AvaloniaMenuInfo menuInfo)
    {
        _menus.RemoveAll(item => item.ViewId.Equals(menuInfo.ViewId, StringComparison.OrdinalIgnoreCase));
        _menus.Add(menuInfo);
    }

    public void RegisterDockPane(AvaloniaDockPaneInfo info, Type viewType, Type viewModelType, Func<IServiceProvider, object>? viewModelFactory = null, bool cacheView = true)
    {
        RegisterRoute(info.ViewId, viewType, viewModelType, viewModelFactory, cacheView);
        _dockPanes.RemoveAll(item => item.ViewId.Equals(info.ViewId, StringComparison.OrdinalIgnoreCase));
        _dockPanes.Add(info);
    }

    public AvaloniaViewRegistration? GetViewRegistration(string viewId)
    {
        return _views.TryGetValue(viewId, out var registration) ? registration : null;
    }

    public IReadOnlyList<AvaloniaMenuInfo> GetAllMenus()
    {
        return _menus.OrderBy(item => item.Order).ToArray();
    }

    public IReadOnlyList<AvaloniaDockPaneInfo> GetAllDockPanes()
    {
        return _dockPanes.ToArray();
    }
}
