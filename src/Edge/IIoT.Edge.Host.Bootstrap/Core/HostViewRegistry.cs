using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Shell.Core;

public sealed class HostViewRegistry : IViewRegistry
{
    private readonly ViewRegistry _inner;

    public HostViewRegistry(IViewRegistry inner)
    {
        _inner = inner as ViewRegistry
            ?? throw new InvalidOperationException("Host routes require the concrete ViewRegistry implementation.");
    }

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
    {
        EnsureCorePrefix(viewId);
        _inner.RegisterCoreRoute(viewId, viewType, viewModelType, cacheView);
    }

    public void RegisterMenu(MenuInfo menuInfo)
    {
        ArgumentNullException.ThrowIfNull(menuInfo);
        EnsureCorePrefix(menuInfo.ViewId);
        _inner.RegisterCoreMenu(menuInfo);
    }

    public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
    {
        ArgumentNullException.ThrowIfNull(info);
        EnsureCorePrefix(info.ContentId);
        _inner.RegisterAnchorable(info, viewType, viewModelType, cacheView);
    }

    public ViewRegistration? GetViewRegistration(string viewId) => _inner.GetViewRegistration(viewId);

    public IReadOnlyList<MenuInfo> GetAllMenus() => _inner.GetAllMenus();

    public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => _inner.GetAllAnchorables();

    private static void EnsureCorePrefix(string viewId)
    {
        if (string.IsNullOrWhiteSpace(viewId)
            || !viewId.StartsWith("Core.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Host routes must use the 'Core.' prefix. Invalid value: '{viewId}'.");
        }
    }
}
