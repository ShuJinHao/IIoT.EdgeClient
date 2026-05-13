using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.UI.Shared.Modularity;

public sealed class ViewRegistration
{
    public required string ViewId { get; init; }
    public required Type ViewType { get; init; }
    public required Type ViewModelType { get; init; }
    public Func<IServiceProvider, ViewModelBase>? ViewModelFactory { get; init; }
    public bool CacheView { get; init; } = true;
}
