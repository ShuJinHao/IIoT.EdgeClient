using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public partial class MainWindow : Window
{
    private const int WindowCornerRadius = 24;
    private const int StartupLeftBiasPixels = 80;

    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            ApplyStartupLeftBias();
            RefreshRoundedWindowRegion();
        };
        SizeChanged += (_, _) => RefreshRoundedWindowRegion();
        PropertyChanged += OnWindowPropertyChanged;
        Closed += (_, _) => ClearRoundedWindowRegion();
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

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            Dispatcher.UIThread.Post(RefreshRoundedWindowRegion);
        }
    }

    private void RefreshRoundedWindowRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = RenderScaling;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        var radius = Math.Max(1, (int)Math.Round(WindowCornerRadius * scale));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        if (region == nint.Zero)
        {
            return;
        }

        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
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

    private void ClearRoundedWindowRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle != nint.Zero)
        {
            SetWindowRgn(handle, nint.Zero, true);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(
        int left,
        int top,
        int right,
        int bottom,
        int widthEllipse,
        int heightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint windowHandle, nint regionHandle, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint objectHandle);
}
