using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.AvaloniaPoc.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
