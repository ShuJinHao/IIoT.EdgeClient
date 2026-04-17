using IIoT.Edge.TestSimulator.Views;
using System.Windows;

namespace IIoT.Edge.TestSimulator.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 日志新增时自动滚动到底部
        viewModel.LogMessages.CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(() => LogListBox.ScrollIntoView(
                LogListBox.Items.Count > 0 ? LogListBox.Items[^1] : null));
    }
}
