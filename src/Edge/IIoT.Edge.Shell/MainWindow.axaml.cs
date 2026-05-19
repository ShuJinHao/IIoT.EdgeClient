using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public MainWindow(
        MainWindowViewModel viewModel,
        NavigationRailView navigationRailView,
        NavigationHostView navigationHostView,
        EquipmentView equipmentView,
        LogView logView)
        : this()
    {
        DataContext = viewModel;
        NavigationRailHost.Content = navigationRailView;
        NavigationContentHost.Content = navigationHostView;
        EquipmentPanelHost.Content = equipmentView;
        LogPanelHost.Content = logView;
    }
}
