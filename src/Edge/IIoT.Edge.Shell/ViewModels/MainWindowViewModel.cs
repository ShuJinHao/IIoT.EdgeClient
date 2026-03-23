using IIoT.Edge.Common.Mvvm;
using IIoT.Edge.Module.Production.Equipment;
using IIoT.Edge.Module.SysLog;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Widgets.Footer;
using IIoT.Edge.UI.Shared.Widgets.Login;
using IIoT.Edge.UI.Shared.Widgets.SysMenu;
using IIoT.Edge.UI.Shared.Widgets.SystemHeader;
using System.Windows;

namespace IIoT.Edge.Shell.ViewModels;

public class MainWindowViewModel : BaseNotifyPropertyChanged
{
    private readonly INavigationService _navigationService;

    public HeaderWidget HeaderViewModel { get; }
    public SysMenuWidget SysMenuViewModel { get; }
    public LoginWidget LoginViewModel { get; }
    public FooterWidget FooterViewModel { get; }
    public LogWidget LogViewModel { get; }
    public EquipmentWidget EquipmentViewModel { get; }

    public FrameworkElement? CurrentView => _navigationService.CurrentView;

    public MainWindowViewModel(
        HeaderWidget headerWidget,
        SysMenuWidget sysMenuWidget,
        LoginWidget loginWidget,
        FooterWidget footerWidget,
        LogWidget logWidget,
        EquipmentWidget equipmentWidget,
        INavigationService navigationService)
    {
        HeaderViewModel = headerWidget;
        SysMenuViewModel = sysMenuWidget;
        LoginViewModel = loginWidget;
        FooterViewModel = footerWidget;
        LogViewModel = logWidget;
        EquipmentViewModel = equipmentWidget;

        _navigationService = navigationService;
        _navigationService.Navigated += _ => OnPropertyChanged(nameof(CurrentView));
    }
}