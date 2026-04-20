using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IIoT.Edge.Shell.Core;

public class NavigationService : INavigationService
{
    private const int MaxCachedViews = 8;

    private readonly IServiceProvider _serviceProvider;
    private readonly IViewRegistry _viewRegistry;
    private readonly ILogService _logger;
    private readonly Dictionary<string, FrameworkElement> _viewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _cacheOrder = new();

    public ViewModelBase? CurrentViewModel { get; private set; }
    public FrameworkElement? CurrentView { get; private set; }

    public event Action<ViewModelBase?>? Navigated;

    public NavigationService(IServiceProvider serviceProvider, IViewRegistry viewRegistry, ILogService logger)
    {
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
        _logger = logger;
    }

    public void NavigateTo(string viewId)
    {
        var registration = _viewRegistry.GetViewRegistration(viewId);
        if (registration is null)
        {
            _logger.Warn($"[Navigation] View registration not found: {viewId}");
            return;
        }

        if (registration.CacheView && _viewCache.TryGetValue(viewId, out var cachedView))
        {
            TouchCacheEntry(viewId);

            if (cachedView.DataContext is not ViewModelBase cachedViewModel)
            {
                _logger.Warn($"[Navigation] Cached view {viewId} has invalid DataContext.");
                InvalidateCache(viewId);
                NavigateTo(viewId);
                return;
            }

            DeactivateCurrentViewModel(viewId, cachedViewModel);
            CurrentViewModel = cachedViewModel;
            CurrentView = cachedView;
            FireAndForgetActivation(viewId, cachedViewModel);
            Navigated?.Invoke(CurrentViewModel);
            return;
        }

        var viewModel = _serviceProvider.GetRequiredService(registration.ViewModelType) as ViewModelBase;
        if (viewModel is null)
        {
            _logger.Warn($"[Navigation] Failed to resolve ViewModel: {registration.ViewModelType.Name}");
            return;
        }

        FrameworkElement view;
        try
        {
            view = (FrameworkElement)ActivatorUtilities.CreateInstance(_serviceProvider, registration.ViewType);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Navigation] Failed to create view {registration.ViewType.Name}: {ex.Message}");
            return;
        }

        view.DataContext = viewModel;

        if (registration.CacheView)
        {
            _viewCache[viewId] = view;
            TouchCacheEntry(viewId);
            TrimCacheIfNeeded(viewId);
        }

        DeactivateCurrentViewModel(viewId, viewModel);
        CurrentViewModel = viewModel;
        CurrentView = view;

        FireAndForgetActivation(viewId, viewModel);
        Navigated?.Invoke(CurrentViewModel);
    }

    public void InvalidateCache(string viewId)
    {
        _viewCache.Remove(viewId);
        RemoveCacheOrderEntry(viewId);
    }

    public void ClearAllCache()
    {
        _viewCache.Clear();
        _cacheOrder.Clear();
    }

    private void TouchCacheEntry(string viewId)
    {
        RemoveCacheOrderEntry(viewId);
        _cacheOrder.AddLast(viewId);
    }

    private void TrimCacheIfNeeded(string currentViewId)
    {
        while (_viewCache.Count > MaxCachedViews && _cacheOrder.First is not null)
        {
            var candidate = _cacheOrder.First.Value;
            _cacheOrder.RemoveFirst();

            if (string.Equals(candidate, currentViewId, StringComparison.OrdinalIgnoreCase))
            {
                _cacheOrder.AddLast(candidate);
                continue;
            }

            _viewCache.Remove(candidate);
        }
    }

    private void RemoveCacheOrderEntry(string viewId)
    {
        var node = _cacheOrder.First;
        while (node is not null)
        {
            var next = node.Next;
            if (string.Equals(node.Value, viewId, StringComparison.OrdinalIgnoreCase))
            {
                _cacheOrder.Remove(node);
                return;
            }

            node = next;
        }
    }

    private void FireAndForgetActivation(string viewId, ViewModelBase viewModel)
    {
        _ = ActivateViewModelAsync(viewId, viewModel);
    }

    private void DeactivateCurrentViewModel(string nextViewId, ViewModelBase nextViewModel)
    {
        if (CurrentViewModel is null || ReferenceEquals(CurrentViewModel, nextViewModel))
        {
            return;
        }

        _ = DeactivateViewModelAsync(nextViewId, CurrentViewModel);
    }

    private async Task ActivateViewModelAsync(string viewId, ViewModelBase viewModel)
    {
        try
        {
            await viewModel.OnActivatedAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Navigation] ViewModel activation failed for {viewId}: {ex.Message}");
        }
    }

    private async Task DeactivateViewModelAsync(string nextViewId, ViewModelBase viewModel)
    {
        try
        {
            await viewModel.OnDeactivatedAsync();
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Navigation] ViewModel deactivation failed before {nextViewId}: {ex.Message}");
        }
    }
}
