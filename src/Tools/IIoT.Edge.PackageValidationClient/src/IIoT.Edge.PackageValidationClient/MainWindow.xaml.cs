using System.Windows;
using IIoT.Edge.PackageValidationClient.ViewModels;

namespace IIoT.Edge.PackageValidationClient;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

