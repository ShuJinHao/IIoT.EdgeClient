using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.Presentation.Shell.Features.Footer;
using IIoT.Edge.Presentation.Shell.Features.Header;
using IIoT.Edge.Presentation.Shell.Features.Login;
using IIoT.Edge.Presentation.Shell.Features.SysMenu;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Windows;

namespace IIoT.Edge.PackageValidationClient.ViewModels;

public class MainWindowViewModel : BaseNotifyPropertyChanged
{
    private readonly INavigationService _navigationService;

    public HeaderViewModel HeaderViewModel { get; }
    public SysMenuViewModel SysMenuViewModel { get; }
    public LoginViewModel LoginViewModel { get; }
    public FooterViewModel FooterViewModel { get; }
    public LogViewModel LogViewModel { get; }
    public EquipmentViewModel EquipmentViewModel { get; }

    public FrameworkElement? CurrentView => _navigationService.CurrentView;

    public MainWindowViewModel(
        HeaderViewModel headerWidget,
        SysMenuViewModel sysMenuWidget,
        LoginViewModel loginWidget,
        FooterViewModel footerWidget,
        LogViewModel logWidget,
        EquipmentViewModel equipmentWidget,
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

