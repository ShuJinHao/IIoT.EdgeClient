using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.Launcher.Avalonia.Views;

public partial class LauncherLoginView : UserControl
{
    public LauncherLoginView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
