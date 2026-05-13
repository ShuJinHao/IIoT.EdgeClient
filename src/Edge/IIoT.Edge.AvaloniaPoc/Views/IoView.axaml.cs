using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.AvaloniaPoc.Views;

public partial class IoView : UserControl
{
    public IoView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
