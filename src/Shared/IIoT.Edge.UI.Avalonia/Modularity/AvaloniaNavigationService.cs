using Avalonia.Controls;

namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaNavigationService : IAvaloniaNavigationService
{
    private readonly IServiceProvider _services;
    private readonly IAvaloniaViewRegistry _viewRegistry;
    private readonly Dictionary<string, Control> _viewCache = new(StringComparer.OrdinalIgnoreCase);

    public AvaloniaNavigationService(IServiceProvider services, IAvaloniaViewRegistry viewRegistry)
    {
        _services = services;
        _viewRegistry = viewRegistry;
    }

    public object? CurrentViewModel { get; private set; }

    public Control? CurrentView { get; private set; }

    public event Action<object?>? Navigated;

    public void NavigateTo(string viewId)
    {
        var registration = _viewRegistry.GetViewRegistration(viewId)
            ?? throw new InvalidOperationException($"View '{viewId}' is not registered.");

        CurrentView = registration.CacheView && _viewCache.TryGetValue(viewId, out var cached)
            ? cached
            : registration.CreateView(_services);

        if (registration.CacheView)
        {
            _viewCache[viewId] = CurrentView;
        }

        CurrentViewModel = CurrentView.DataContext;
        Navigated?.Invoke(CurrentViewModel);
    }
}
