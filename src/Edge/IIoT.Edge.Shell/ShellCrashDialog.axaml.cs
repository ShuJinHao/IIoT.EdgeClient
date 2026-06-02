using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.Shell;

public partial class ShellCrashDialog : Window
{
    public ShellCrashDialog()
        : this(ResourceText("Shell_CrashDialogDefaultMessage", string.Empty))
    {
    }

    public ShellCrashDialog(string message)
    {
        InitializeComponent();
        DataContext = new ShellCrashDialogViewModel(message);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string ResourceText(string key, string fallback)
    {
        var app = global::Avalonia.Application.Current;
        return app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : fallback;
    }

    private sealed record ShellCrashDialogViewModel(string Message);
}
