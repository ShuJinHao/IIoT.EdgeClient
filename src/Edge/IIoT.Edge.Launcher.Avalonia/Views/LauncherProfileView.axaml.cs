using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.Launcher.Avalonia.Views;

public partial class LauncherProfileView : UserControl
{
    public LauncherProfileView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
