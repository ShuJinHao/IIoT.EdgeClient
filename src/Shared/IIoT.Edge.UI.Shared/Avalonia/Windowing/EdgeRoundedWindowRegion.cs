using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace IIoT.Edge.UI.Shared.Avalonia.Windowing;

public static class EdgeRoundedWindowRegion
{
    public static IDisposable Attach(Window window, double cornerRadius)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (cornerRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cornerRadius), "Window corner radius must be positive.");
        }

        var subscription = new RoundedWindowRegionSubscription(window, cornerRadius);
        subscription.Attach();
        return subscription;
    }

    private sealed class RoundedWindowRegionSubscription(Window window, double cornerRadius) : IDisposable
    {
        private bool disposed;

        public void Attach()
        {
            window.Opened += OnOpened;
            window.SizeChanged += OnSizeChanged;
            window.PropertyChanged += OnWindowPropertyChanged;
            window.Closed += OnClosed;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            window.Opened -= OnOpened;
            window.SizeChanged -= OnSizeChanged;
            window.PropertyChanged -= OnWindowPropertyChanged;
            window.Closed -= OnClosed;
            Clear();
        }

        private void OnOpened(object? sender, EventArgs e)
            => Refresh();

        private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
            => Refresh();

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty)
            {
                Dispatcher.UIThread.Post(Refresh);
            }
        }

        private void OnClosed(object? sender, EventArgs e)
            => Dispose();

        private void Refresh()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle == nint.Zero || window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
            {
                return;
            }

            var scale = window.RenderScaling > 0 ? window.RenderScaling : 1;
            var width = Math.Max(1, (int)Math.Ceiling(window.Bounds.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(window.Bounds.Height * scale));
            var radius = Math.Max(1, (int)Math.Round(cornerRadius * scale));
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

        private void Clear()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (handle != nint.Zero)
            {
                SetWindowRgn(handle, nint.Zero, true);
            }
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
