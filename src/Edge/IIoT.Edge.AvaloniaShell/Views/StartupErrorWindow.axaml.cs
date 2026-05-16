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
            string.IsNullOrWhiteSpace(diagnosticsSummary)
                ? Text("Shell_StartupError_DiagnosticsSummaryMissing")
                : diagnosticsSummary,
            string.IsNullOrWhiteSpace(diagnosticsLogPath)
                ? Text("Shell_StartupError_DiagnosticsLogPathMissing")
                : diagnosticsLogPath);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string Text(string resourceKey)
        => global::Avalonia.Application.Current?.Resources.TryGetResource(resourceKey, null, out var value) == true &&
           value is string text
            ? text
            : resourceKey;

    private sealed record StartupErrorViewModel(
        string Message,
        string DiagnosticsSummary,
        string DiagnosticsLogPath);
}
