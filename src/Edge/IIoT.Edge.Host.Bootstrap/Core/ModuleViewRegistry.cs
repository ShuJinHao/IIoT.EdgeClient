using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Shell.Core;

public sealed class ModuleViewRegistry : IViewRegistry
{
    private readonly IViewRegistry _inner;
    private readonly string _moduleId;
    private readonly string _requiredPrefix;

    public ModuleViewRegistry(IViewRegistry inner, string moduleId)
    {
        _inner = inner;
        _moduleId = moduleId;
        _requiredPrefix = $"{moduleId}.";
    }

    public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
    {
        EnsureModulePrefix(viewId, nameof(viewId));
        _inner.RegisterRoute(viewId, viewType, viewModelType, cacheView);
    }

    public void RegisterMenu(MenuInfo menuInfo)
    {
        ArgumentNullException.ThrowIfNull(menuInfo);
        EnsureModulePrefix(menuInfo.ViewId, $"{nameof(menuInfo)}.{nameof(menuInfo.ViewId)}");
        _inner.RegisterMenu(menuInfo);
    }

    public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
    {
        ArgumentNullException.ThrowIfNull(info);
        EnsureModulePrefix(info.ContentId, $"{nameof(info)}.{nameof(info.ContentId)}");
        _inner.RegisterAnchorable(info, viewType, viewModelType, cacheView);
    }

    public ViewRegistration? GetViewRegistration(string viewId) => _inner.GetViewRegistration(viewId);

    public IReadOnlyList<MenuInfo> GetAllMenus() => _inner.GetAllMenus();

    public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => _inner.GetAllAnchorables();

    private void EnsureModulePrefix(string viewId, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(viewId)
            || !viewId.StartsWith(_requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Module '{_moduleId}' can only register view ids prefixed with '{_requiredPrefix}'. Invalid value: '{viewId}'.");
        }
    }
}
