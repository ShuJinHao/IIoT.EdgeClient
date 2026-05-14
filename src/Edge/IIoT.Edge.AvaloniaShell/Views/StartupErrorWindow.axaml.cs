using Avalonia.Controls;
using Avalonia.Interactivity;

namespace IIoT.Edge.AvaloniaShell.Views;

public partial class StartupErrorWindow : Window
{
    public StartupErrorWindow()
    {
        InitializeComponent();
    }

    public StartupErrorWindow(string message)
        : this()
    {
        DataContext = message;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

