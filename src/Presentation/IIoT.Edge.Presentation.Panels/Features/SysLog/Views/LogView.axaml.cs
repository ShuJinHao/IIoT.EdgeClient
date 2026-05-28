using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public LogView(LogViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
