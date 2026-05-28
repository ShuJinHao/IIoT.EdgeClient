using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using IIoT.Edge.Application.Features.Production.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public partial class ProductionPlanSelectionWindow : Window
{
    private const int WindowCornerRadius = 28;

    public event Action<ProductionPlanOption?>? Completed;

    public ProductionPlanSelectionWindow()
    {
        InitializeComponent();
        Opened += (_, _) => RefreshRoundedWindowRegion();
        SizeChanged += (_, _) => RefreshRoundedWindowRegion();
        Closed += (_, _) => ClearRoundedWindowRegion();
    }

    [ActivatorUtilitiesConstructor]
    public ProductionPlanSelectionWindow(ProductionPlanSelectionWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ProductionPlanSelectionWindowViewModel { SelectedPlan: ProductionPlanOption plan })
        {
            Completed?.Invoke(plan);
            Close();
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Completed?.Invoke(null);
        Close();
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
