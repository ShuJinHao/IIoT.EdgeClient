using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public partial class OverviewWorkspaceView : UserControl
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly IViewRegistry? _viewRegistry;
    private readonly OverviewWorkspaceViewModel? _viewModel;
    private readonly Dictionary<string, Control> _viewCache = new(StringComparer.OrdinalIgnoreCase);

    public OverviewWorkspaceView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public OverviewWorkspaceView(
        IServiceProvider serviceProvider,
        IViewRegistry viewRegistry,
        OverviewWorkspaceViewModel viewModel)
        : this()
    {
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
        _viewModel = viewModel;
        DataContext = viewModel;
        OverviewTabsHost.DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyContent(_viewModel.SelectedTab);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverviewWorkspaceViewModel.SelectedTab))
        {
            ApplyContent(_viewModel?.SelectedTab);
        }
    }

    private void ApplyContent(OverviewTabItemViewModel? tab)
    {
        if (_serviceProvider is null || tab is null)
        {
            return;
        }

        if (tab.ViewId == OverviewWorkspaceViewModel.TodayOverviewViewId)
        {
            OverviewContentHost.Content = GetOrCreate("Overview.Today", () => _serviceProvider.GetRequiredService<DashboardPreviewView>());
            return;
        }

        if (string.IsNullOrWhiteSpace(tab.ViewId))
        {
            OverviewContentHost.Content = CreateMissingView(tab);
            return;
        }

        OverviewContentHost.Content = ResolveRouteView(tab.ViewId) ?? CreateMissingView(tab);
    }

    private Control? ResolveRouteView(string viewId)
    {
        if (_serviceProvider is null || _viewRegistry is null)
        {
            return null;
        }

        if (string.Equals(viewId, "Production.CapacityView", StringComparison.OrdinalIgnoreCase))
        {
            return GetOrCreate(viewId, () => _serviceProvider.GetRequiredService<CapacityViewPage>());
        }

        var registration = _viewRegistry.GetViewRegistration(viewId);
        if (registration is null)
        {
            Warn($"[Overview] View registration not found: {viewId}");
            return null;
        }

        if (registration.CacheView && _viewCache.TryGetValue(viewId, out var cachedView))
        {
            return cachedView;
        }

        object viewModel;
        try
        {
            viewModel = registration.ViewModelFactory?.Invoke(_serviceProvider)
                ?? _serviceProvider.GetRequiredService(registration.ViewModelType);
        }
        catch (Exception ex)
        {
            Warn($"[Overview] Failed to resolve ViewModel for {viewId}: {ex.Message}");
            return null;
        }

        object view;
        try
        {
            view = ActivatorUtilities.CreateInstance(_serviceProvider, registration.ViewType, viewModel);
        }
        catch (Exception ex)
        {
            Warn($"[Overview] Failed to create view for {viewId}: {ex.Message}");
            return null;
        }

        if (view is not Control control)
        {
            Warn($"[Overview] Registered view is not an Avalonia Control: {registration.ViewType.FullName}");
            return null;
        }

        control.DataContext ??= viewModel;

        if (registration.CacheView)
        {
            _viewCache[viewId] = control;
        }

        return control;
    }

    private Control CreateMissingView(OverviewTabItemViewModel tab)
    {
        var isCapacity = string.Equals(tab.Key, "Overview.Capacity", StringComparison.OrdinalIgnoreCase);
        return new PlaceholderPageView
        {
            Title = isCapacity ? _viewModel?.MissingCapacityTitle ?? string.Empty : _viewModel?.MissingDataTitle ?? string.Empty,
            Description = isCapacity ? _viewModel?.MissingCapacityDescription ?? string.Empty : _viewModel?.MissingDataDescription ?? string.Empty
        };
    }

    private Control GetOrCreate(string key, Func<Control> factory)
    {
        if (_viewCache.TryGetValue(key, out var cachedView))
        {
            return cachedView;
        }

        var view = factory();
        _viewCache[key] = view;
        return view;
    }

    private static void Warn(string message)
        => Trace.TraceWarning(message);
}
