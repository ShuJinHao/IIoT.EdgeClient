using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.AvaloniaPoc.Views;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
