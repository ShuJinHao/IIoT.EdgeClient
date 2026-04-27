using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.Presentation.Shell.Features.Footer;
using IIoT.Edge.Presentation.Shell.Features.Login;
using IIoT.Edge.Presentation.Shell.Features.SysMenu;
using IIoT.Edge.Presentation.Shell.Features.Header;
using IIoT.Edge.Presentation.Shell.Localization;
using System.Windows;

namespace IIoT.Edge.Shell.ViewModels;

public class MainWindowViewModel : BaseNotifyPropertyChanged
{
    private readonly INavigationService _navigationService;
    private readonly IAppLanguageService _languageService;

    public HeaderViewModel HeaderViewModel { get; }
    public SysMenuViewModel SysMenuViewModel { get; }
    public LoginViewModel LoginViewModel { get; }
    public FooterViewModel FooterViewModel { get; }
    public LogViewModel LogViewModel { get; }
    public EquipmentViewModel EquipmentViewModel { get; }

    public FrameworkElement? CurrentView => _navigationService.CurrentView;
    public string MainWorkspaceTitle => _languageService.GetString("Shell_MainWorkspace", "主工作区");
    public string EquipmentPanelTitle => _languageService.GetString("Shell_EquipmentInfo", "设备信息");
    public string SystemLogPanelTitle => _languageService.GetString("Shell_SystemLog", "系统日志");

    public MainWindowViewModel(
        HeaderViewModel headerWidget,
        SysMenuViewModel sysMenuWidget,
        LoginViewModel loginWidget,
        FooterViewModel footerWidget,
        LogViewModel logWidget,
        EquipmentViewModel equipmentWidget,
        INavigationService navigationService,
        IAppLanguageService languageService)
    {
        HeaderViewModel = headerWidget;
        SysMenuViewModel = sysMenuWidget;
        LoginViewModel = loginWidget;
        FooterViewModel = footerWidget;
        LogViewModel = logWidget;
        EquipmentViewModel = equipmentWidget;

        _navigationService = navigationService;
        _languageService = languageService;
        _navigationService.Navigated += _ => OnPropertyChanged(nameof(CurrentView));
        _languageService.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(MainWorkspaceTitle));
            OnPropertyChanged(nameof(EquipmentPanelTitle));
            OnPropertyChanged(nameof(SystemLogPanelTitle));
        };
    }
}

