using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public partial class NavigationRailView : UserControl
{
    public NavigationRailView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public NavigationRailView(NavigationRailViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }
}
