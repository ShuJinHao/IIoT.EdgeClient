using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IIoT.Edge.Shell.Core;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IViewRegistry _viewRegistry;

    public WidgetBase? CurrentWidget { get; private set; }
    public FrameworkElement? CurrentView { get; private set; }

    public event Action<WidgetBase?>? Navigated;

    public NavigationService(IServiceProvider serviceProvider, IViewRegistry viewRegistry)
    {
        _serviceProvider = serviceProvider;
        _viewRegistry = viewRegistry;
    }

    public void NavigateTo(string widgetId)
    {
        var widgetType = _viewRegistry.GetWidgetType(widgetId);
        if (widgetType is null)
        {
            System.Diagnostics.Debug.WriteLine($"[NavigationService] 找不到 WidgetId: {widgetId}");
            return;
        }

        var widget = _serviceProvider.GetRequiredService(widgetType) as WidgetBase;
        if (widget is null)
        {
            System.Diagnostics.Debug.WriteLine($"[NavigationService] Resolve失败: {widgetType.Name}");
            return;
        }

        var viewType = _viewRegistry.GetViewType(widgetId);
        if (viewType is null)
        {
            System.Diagnostics.Debug.WriteLine($"[NavigationService] 找不到ViewType: {widgetId}");
            return;
        }

        var view = Activator.CreateInstance(viewType) as FrameworkElement;
        if (view is null)
        {
            System.Diagnostics.Debug.WriteLine($"[NavigationService] 创建View失败: {viewType.Name}");
            return;
        }

        view.DataContext = widget;
        CurrentWidget = widget;
        CurrentView = view;
        Navigated?.Invoke(CurrentWidget);
    }
}