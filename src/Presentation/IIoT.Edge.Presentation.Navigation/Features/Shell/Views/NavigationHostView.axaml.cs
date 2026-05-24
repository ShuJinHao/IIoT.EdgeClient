using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public partial class NavigationHostView : UserControl
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly NavigationRailViewModel? _railViewModel;
    private readonly IViewRegistry? _viewRegistry;
    private readonly Dictionary<string, Control> _dynamicViewCache = new(StringComparer.OrdinalIgnoreCase);

    public NavigationHostView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public NavigationHostView(
        IServiceProvider serviceProvider,
        NavigationRailViewModel railViewModel,
        IViewRegistry viewRegistry)
        : this()
    {
        _serviceProvider = serviceProvider;
        _railViewModel = railViewModel;
        _viewRegistry = viewRegistry;
        _railViewModel.PropertyChanged += OnRailPropertyChanged;
        ApplyContent(_railViewModel.SelectedItem);
    }

    private void OnRailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationRailViewModel.SelectedItem))
        {
            ApplyContent(_railViewModel?.SelectedItem);
        }
    }

    private void ApplyContent(NavigationItemViewModel? item)
    {
        if (_serviceProvider is null || item is null)
        {
            return;
        }

        DashboardContentHost.Content = item.ViewId switch
        {
            CoreViewIds.Dashboard => _serviceProvider.GetRequiredService<OverviewWorkspaceView>(),
            CoreViewIds.ShellMonitor => new PlaceholderPageView
            {
                Title = "监控功能建设中",
                Description = "监控页将在后续批次按首屏母版接入真实页面。"
            },
            CoreViewIds.ShellOperations => new PlaceholderPageView
            {
                Title = "运维功能建设中",
                Description = "运维页将在后续批次按首屏母版接入真实页面。"
            },
            CoreViewIds.ShellConfiguration => new PlaceholderPageView
            {
                Title = "配置功能建设中",
                Description = "配置页将在后续批次按首屏母版接入真实页面。"
            },
            "Formula.RecipeView" => _serviceProvider.GetRequiredService<RecipeViewPage>(),
            "Config.ParamView" => _serviceProvider.GetRequiredService<ParamViewPage>(),
            CoreViewIds.Diagnostics => _serviceProvider.GetRequiredService<DiagnosticsPage>(),
            "Production.DataView" => _serviceProvider.GetRequiredService<DataViewPage>(),
            "Production.CapacityView" => _serviceProvider.GetRequiredService<CapacityViewPage>(),
            "Production.Monitor" => _serviceProvider.GetRequiredService<MonitorViewPage>(),
            "Hardware.IOView" => _serviceProvider.GetRequiredService<IOViewPage>(),
            "Hardware.HardwareConfigView" => _serviceProvider.GetRequiredService<HardwareConfigPage>(),
            "Hardware.PlcTaskBindingView" => _serviceProvider.GetRequiredService<PlcTaskBindingPage>(),
            _ => ResolveDynamicView(item.ViewId)
        };
    }

    private Control? ResolveDynamicView(string viewId)
    {
        if (_serviceProvider is null || _viewRegistry is null)
        {
            return null;
        }

        var registration = _viewRegistry.GetViewRegistration(viewId);
        if (registration is null)
        {
            Warn($"[Navigation] View registration not found: {viewId}");
            return null;
        }

        if (registration.CacheView && _dynamicViewCache.TryGetValue(viewId, out var cachedView))
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
            Warn($"[Navigation] Failed to resolve ViewModel for {viewId}: {ex.Message}");
            return null;
        }

        object view;
        try
        {
            view = ActivatorUtilities.CreateInstance(_serviceProvider, registration.ViewType, viewModel);
        }
        catch (Exception ex)
        {
            Warn($"[Navigation] Failed to create view for {viewId}: {ex.Message}");
            return null;
        }

        if (view is not Control control)
        {
            Warn($"[Navigation] Registered view is not an Avalonia Control: {registration.ViewType.FullName}");
            return null;
        }

        control.DataContext ??= viewModel;

        if (registration.CacheView)
        {
            _dynamicViewCache[viewId] = control;
        }

        return control;
    }

    private static void Warn(string message)
        => Debug.WriteLine(message);
}
