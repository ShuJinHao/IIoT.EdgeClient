using Avalonia.Markup.Xaml;
using SukiUI.Controls;

namespace IIoT.Edge.AvaloniaShell.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
