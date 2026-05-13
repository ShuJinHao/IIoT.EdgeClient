using System.Windows;
using IIoT.Edge.Shell.ViewModels;

namespace IIoT.Edge.Shell
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
