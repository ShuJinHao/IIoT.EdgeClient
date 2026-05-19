using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

/// <summary>
/// 诊断死信操作确认对话框。
/// </summary>
public partial class DiagnosticsConfirmationDialog : Window
{
    public DiagnosticsConfirmationDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public DiagnosticsConfirmationDialog(string title, string message)
    {
        InitializeComponent();
        DataContext = new DiagnosticsConfirmationDialogViewModel(title, message);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private sealed record DiagnosticsConfirmationDialogViewModel(string Title, string Message);
}
