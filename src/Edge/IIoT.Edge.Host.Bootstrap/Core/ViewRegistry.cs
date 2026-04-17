using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Shell.Core;

public class ViewRegistry : IViewRegistry
{
    private readonly Dictionary<string, ViewRegistration> _views = new();
    private readonly List<MenuInfo> _menus = new();
    private readonly List<AnchorableInfo> _anchorables = new();

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
    {
        EnsureRouteDoesNotUseCorePrefix(viewId);
        RegisterRouteCore(viewId, viewType, viewModelType, cacheView);
    }

    internal void RegisterCoreRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
    {
        RegisterRouteCore(viewId, viewType, viewModelType, cacheView);
    }

    private void RegisterRouteCore(string viewId, Type viewType, Type viewModelType, bool cacheView)
    {
        if (_views.ContainsKey(viewId))
        {
            throw new InvalidOperationException($"View id '{viewId}' is already registered.");
        }

        _views[viewId] = new ViewRegistration
        {
            ViewId = viewId,
            ViewType = viewType,
            ViewModelType = viewModelType,
            CacheView = cacheView
        };
    }

    public void RegisterMenu(MenuInfo menuInfo)
    {
        EnsureRouteDoesNotUseCorePrefix(menuInfo.ViewId);
        RegisterMenuCore(menuInfo);
    }

    internal void RegisterCoreMenu(MenuInfo menuInfo)
    {
        RegisterMenuCore(menuInfo);
    }

    private void RegisterMenuCore(MenuInfo menuInfo)
    {
        if (_menus.Any(x => string.Equals(x.ViewId, menuInfo.ViewId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Menu view id '{menuInfo.ViewId}' is already registered.");
        }

        _menus.Add(menuInfo);
    }

    public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
    {
        if (_views.ContainsKey(info.ContentId))
        {
            throw new InvalidOperationException($"View id '{info.ContentId}' is already registered.");
        }

        _anchorables.Add(info);
        _views[info.ContentId] = new ViewRegistration
        {
            ViewId = info.ContentId,
            ViewType = viewType,
            ViewModelType = viewModelType,
            CacheView = cacheView
        };
    }

    public ViewRegistration? GetViewRegistration(string viewId)
    {
        _views.TryGetValue(viewId, out var registration);
        return registration;
    }

    public IReadOnlyList<ViewRegistration> GetAllViewRegistrations()
        => _views.Values
            .OrderBy(x => x.ViewId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<MenuInfo> GetAllMenus() => _menus.AsReadOnly();

    public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => _anchorables.AsReadOnly();

    private static void EnsureRouteDoesNotUseCorePrefix(string viewId)
    {
        if (viewId.StartsWith("Core.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Core-prefixed view id '{viewId}' can only be registered through anchorable host panels.");
        }
    }
}
