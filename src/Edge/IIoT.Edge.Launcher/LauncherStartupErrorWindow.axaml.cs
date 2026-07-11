using Avalonia.Controls;
using Avalonia.Interactivity;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;

namespace IIoT.Edge.Launcher;

public partial class LauncherStartupErrorWindow : Window
{
    private const int WindowCornerRadius = 16;
    internal const string FallbackWindowTitle = "IIoT Edge Launcher";
    internal const string FallbackTitle = "启动失败";
    internal const string FallbackMessage = "本地启动器初始化失败。";
    internal const string FallbackCloseText = "关闭";

    public LauncherStartupErrorWindow()
        : this(FallbackWindowTitle, FallbackTitle, FallbackMessage, FallbackCloseText)
    {
    }

    public LauncherStartupErrorWindow(
        string windowTitle,
        string title,
        string message,
        string closeText)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        DataContext = new StartupErrorViewModel(
            Fallback(windowTitle, FallbackWindowTitle),
            Fallback(title, FallbackTitle),
            Fallback(message, FallbackMessage),
            Fallback(closeText, FallbackCloseText));
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string Fallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private sealed record StartupErrorViewModel(
        string WindowTitle,
        string Title,
        string Message,
        string CloseText);
}
