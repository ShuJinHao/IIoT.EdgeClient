using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.AvaloniaShell.Views;

public partial class StartupErrorWindow : Window
{
    private const string DeviceModuleMismatchCode = "DEVICE_MODULE_MISMATCH";

    public StartupErrorWindow()
    {
        InitializeComponent();
    }

    public StartupErrorWindow(string message, string? diagnosticsSummary = null, string? diagnosticsLogPath = null)
        : this()
    {
        DataContext = CreateViewModel(
            message,
            string.IsNullOrWhiteSpace(diagnosticsSummary)
                ? Text("Shell_StartupError_DiagnosticsSummaryMissing")
                : diagnosticsSummary,
            string.IsNullOrWhiteSpace(diagnosticsLogPath)
                ? Text("Shell_StartupError_DiagnosticsLogPathMissing")
                : diagnosticsLogPath);
    }

    public static Task ShowStartupFailureAsync(
        Window? owner,
        string message,
        string? diagnosticsSummary = null,
        string? diagnosticsLogPath = null)
        => new StartupErrorWindow(message, diagnosticsSummary, diagnosticsLogPath)
            .ShowSafelyAsync(owner);

    public async Task ShowSafelyAsync(Window? owner)
    {
        if (owner?.IsVisible == true)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await ShowDialog(owner);
            return;
        }

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var closed = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleClosed(object? sender, EventArgs e)
        {
            Closed -= HandleClosed;
            closed.TrySetResult(null);
        }

        Closed += HandleClosed;

        try
        {
            Show();
            await closed.Task;
        }
        catch
        {
            Closed -= HandleClosed;
            throw;
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static StartupErrorViewModel CreateViewModel(
        string message,
        string diagnosticsSummary,
        string diagnosticsLogPath)
    {
        var isModuleMismatch = message.Contains(
            DeviceModuleMismatchCode,
            StringComparison.OrdinalIgnoreCase);

        var deviceName = isModuleMismatch
            ? ExtractDelimitedValue(message, "设备=") ?? "--"
            : "--";
        var referencedModule = isModuleMismatch
            ? ExtractDelimitedValue(message, "模块=") ?? "--"
            : "--";
        var loadedModuleCount = isModuleMismatch
            ? ExtractDelimitedValue(diagnosticsSummary, "模块数：", "模块数:") ?? "--"
            : "--";

        return new StartupErrorViewModel(
            isModuleMismatch
                ? Text("Shell_StartupError_ModuleMismatchOperatorMessage")
                : Text("Shell_StartupError_OperatorMessage"),
            isModuleMismatch
                ? BuildModuleMismatchIssue(deviceName, referencedModule, loadedModuleCount)
                : Text("Shell_StartupError_GenericIssue"),
            message,
            diagnosticsSummary,
            diagnosticsLogPath,
            deviceName,
            referencedModule,
            loadedModuleCount);
    }

    private static string BuildModuleMismatchIssue(
        string deviceName,
        string referencedModule,
        string loadedModuleCount)
        => FormatText(
            "Shell_StartupError_ModuleMismatchIssueFormat",
            deviceName,
            referencedModule,
            loadedModuleCount);

    private static string? ExtractDelimitedValue(string source, params string[] labels)
    {
        foreach (var label in labels)
        {
            var start = source.IndexOf(label, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += label.Length;
            var end = source.IndexOfAny(new[] { ',', '，', ')', '）', ';', '；', '\r', '\n' }, start);
            return source[start..(end < 0 ? source.Length : end)].Trim();
        }

        return null;
    }

    private static string Text(string resourceKey)
        => global::Avalonia.Application.Current?.Resources.TryGetResource(resourceKey, null, out var value) == true &&
           value is string text
            ? text
            : resourceKey;

    private static string FormatText(string resourceKey, params object[] args)
    {
        var template = Text(resourceKey);
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private sealed record StartupErrorViewModel(
        string OperatorMessage,
        string PrimaryIssue,
        string TechnicalDetails,
        string DiagnosticsSummary,
        string DiagnosticsLogPath,
        string DeviceName,
        string ReferencedModule,
        string LoadedModuleCount);
}
