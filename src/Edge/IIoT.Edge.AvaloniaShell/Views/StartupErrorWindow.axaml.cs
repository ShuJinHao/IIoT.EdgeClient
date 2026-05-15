using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.AvaloniaShell.Views;

public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow()
    {
        InitializeComponent();
    }

    public StartupErrorWindow(string message, string? diagnosticsSummary = null, string? diagnosticsLogPath = null)
        : this()
    {
        DataContext = new StartupErrorViewModel(
            message,
            string.IsNullOrWhiteSpace(diagnosticsSummary) ? "启动诊断尚未生成。" : diagnosticsSummary,
            string.IsNullOrWhiteSpace(diagnosticsLogPath) ? "诊断日志路径尚未生成。" : diagnosticsLogPath);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed record StartupErrorViewModel(
        string Message,
        string DiagnosticsSummary,
        string DiagnosticsLogPath);
}
