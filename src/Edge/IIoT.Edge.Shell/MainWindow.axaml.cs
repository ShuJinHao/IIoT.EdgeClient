using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Shell.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public partial class MainWindow : Window
{
    private const int WindowCornerRadius = 24;
    private const int StartupLeftBiasPixels = 80;

    public MainWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        Opened += (_, _) => ApplyStartupLeftBias();
    }

    [ActivatorUtilitiesConstructor]
    public MainWindow(
        MainWindowViewModel viewModel,
        NavigationRailView navigationRailView,
        NavigationHostView navigationHostView,
        EquipmentView equipmentView,
        LogView logView)
        : this()
    {
        DataContext = viewModel;
        NavigationRailHost.Content = navigationRailView;
        NavigationContentHost.Content = navigationHostView;
        EquipmentPanelHost.Content = equipmentView;
        LogPanelHost.Content = logView;
    }

    private void ApplyStartupLeftBias()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = RenderScaling > 0 ? RenderScaling : 1;
        var area = screen.WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        var centeredX = area.X + Math.Max(0, (area.Width - width) / 2);
        var centeredY = area.Y + Math.Max(0, (area.Height - height) / 2);
        var x = Math.Max(area.X, centeredX - StartupLeftBiasPixels);

        Position = new PixelPoint(x, centeredY);
    }
}
