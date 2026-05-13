using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.AvaloniaShell.Views;

public partial class IoView : UserControl
{
    public IoView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
