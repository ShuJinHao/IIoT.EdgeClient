using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaWindowService : IAvaloniaWindowService
{
    public string MaxRestoreIcon => ActiveWindow?.WindowState == WindowState.Maximized
        ? "WindowRestore"
        : "WindowMaximize";

    public event EventHandler? StateChanged;

    public void Minimize()
    {
        if (ActiveWindow is { } window)
        {
            window.WindowState = WindowState.Minimized;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ToggleMaximize()
    {
        if (ActiveWindow is not { } window)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        ActiveWindow?.Close();
    }

    private static Window? ActiveWindow
    {
        get
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return null;
            }

            return desktop.Windows.FirstOrDefault(window => window.IsActive)
                ?? desktop.MainWindow;
        }
    }
}
