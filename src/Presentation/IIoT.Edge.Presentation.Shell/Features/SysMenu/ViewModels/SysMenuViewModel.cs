using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Shell.Features.SysMenu;

/// <summary>
/// 系统菜单视图模型。
/// 负责构建左侧菜单、处理导航以及响应登录状态变化。
/// </summary>
public class SysMenuViewModel : ViewModelBase
{
    public override string ViewId => "Core.SysMenu";
    public override string ViewTitle => "系统导航菜单";

    private readonly INavigationService _navigationService;
    private readonly IAuthService _authService;
    private readonly IClientPermissionService _permissionService;

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new();

    private MenuItemViewModel? _selectedItem;
    public MenuItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
    }

    public ICommand NavigateCommand { get; }
    public ICommand LoginCommand { get; }

    public string LoginButtonText => _authService.IsAuthenticated
        ? $"注销 ({_authService.CurrentUser?.DisplayName})"
        : "登录";

    public SysMenuViewModel(
        INavigationService navigationService,
        IAuthService authService,
        IClientPermissionService permissionService,
        IViewRegistry viewRegistry)
    {
        _navigationService = navigationService;
        _authService = authService;
        _permissionService = permissionService;

        LayoutRow = 1;
        LayoutColumn = 0;

        NavigateCommand = new BaseCommand(ExecuteNavigate);
        LoginCommand = new BaseCommand(_ => ExecuteLogin());

        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;

        BuildMenuItemsFromRegistry(viewRegistry);
    }

    private void BuildMenuItemsFromRegistry(IViewRegistry viewRegistry)
    {
        MenuItems.Clear();
        foreach (var menu in viewRegistry.GetAllMenus().OrderBy(m => m.Order))
            MenuItems.Add(new MenuItemViewModel(menu, _permissionService));
    }

    private void RefreshMenuPermissions()
    {
        foreach (var item in MenuItems)
            item.RefreshPermission();
    }

    private void ExecuteNavigate(object? parameter)
    {
        if (parameter is not string viewId) return;
        if (string.IsNullOrEmpty(viewId)) return;

        var menuItem = MenuItems.FirstOrDefault(m => m.ViewId == viewId);
        if (menuItem is null || !menuItem.IsAccessible) return;

        if (SelectedItem is not null)
            SelectedItem.IsSelected = false;

        SelectedItem = menuItem;
        SelectedItem.IsSelected = true;

        _navigationService.NavigateTo(viewId);
    }

    private void ExecuteLogin()
    {
        if (_authService.IsAuthenticated)
            _authService.Logout();
        else
            DialogHost.OpenDialogCommand.Execute(null, null);
    }

    private void HandlePermissionStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshMenuPermissions();
            OnPropertyChanged(nameof(LoginButtonText));
            return;
        }

        dispatcher.Invoke(() =>
        {
            RefreshMenuPermissions();
            OnPropertyChanged(nameof(LoginButtonText));
        });
    }
}
