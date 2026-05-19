using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware;

/// <summary>
/// 硬件配置危险操作确认对话框。
/// </summary>
public partial class HardwareConfirmationDialog : Window
{
    public HardwareConfirmationDialog()
        : this(string.Empty, string.Empty)
    {
    }

    public HardwareConfirmationDialog(string title, string message)
    {
        InitializeComponent();
        DataContext = new HardwareConfirmationDialogViewModel(title, message);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private sealed record HardwareConfirmationDialogViewModel(string DialogTitle, string Message);
}
