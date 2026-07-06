using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Configuration;

public partial class ConfigurationWorkspaceView : UserControl
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly IViewRegistry? _viewRegistry;
    private readonly ConfigurationWorkspaceViewModel? _viewModel;
    private readonly Dictionary<string, Control> _viewCache = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationWorkspaceView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public ConfigurationWorkspaceView(
        IServiceProvider serviceProvider,
        IViewRegistry viewRegistry,
        ConfigurationWorkspaceViewModel viewModel)
        : this()
    {
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
        _viewModel = viewModel;
        DataContext = viewModel;
        ConfigurationTabsHost.DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyContent(_viewModel.SelectedTab);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigurationWorkspaceViewModel.SelectedTab)
            or nameof(ConfigurationWorkspaceViewModel.IsContentVisible))
        {
            ApplyContent(_viewModel?.SelectedTab);
        }
    }

    private void ApplyContent(ConfigurationWorkspaceTabItemViewModel? tab)
    {
        if (_serviceProvider is null || tab is null || !tab.HasPermission)
        {
            ConfigurationContentHost.Content = null;
            return;
        }

        ConfigurationContentHost.Content = ResolveRouteView(tab.ViewId);
    }

    private Control? ResolveRouteView(string viewId)
    {
        if (_serviceProvider is null || _viewRegistry is null)
        {
            return null;
        }

        var registration = _viewRegistry.GetViewRegistration(viewId);
        if (registration is null)
        {
            Warn($"[Configuration] View registration not found: {viewId}");
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
            Warn($"[Configuration] Failed to resolve ViewModel for {viewId}: {ex.Message}");
            return null;
        }

        object view;
        try
        {
            view = ActivatorUtilities.CreateInstance(_serviceProvider, registration.ViewType, viewModel);
        }
        catch (Exception ex)
        {
            Warn($"[Configuration] Failed to create view for {viewId}: {ex.Message}");
            return null;
        }

        if (view is not Control control)
        {
            Warn($"[Configuration] Registered view is not an Avalonia Control: {registration.ViewType.FullName}");
            return null;
        }

        control.DataContext ??= viewModel;

        if (registration.CacheView)
        {
            _viewCache[viewId] = control;
        }

        return control;
    }

    private static void Warn(string message)
        => Trace.TraceWarning(message);
}
