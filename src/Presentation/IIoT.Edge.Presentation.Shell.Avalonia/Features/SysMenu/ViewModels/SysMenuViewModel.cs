using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Shell.Avalonia.Features.SysMenu.ViewModels;

public sealed partial class SysMenuViewModel : AvaloniaViewModelBase
{
    private readonly IAvaloniaNavigationService _navigationService;
    private readonly IAuthService _authService;
    private readonly IClientPermissionService _permissionService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDispatcherService _dispatcherService;

    public SysMenuViewModel(
        IAvaloniaNavigationService navigationService,
        IAuthService authService,
        IClientPermissionService permissionService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDispatcherService dispatcherService,
        IAvaloniaViewRegistry viewRegistry)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));

        foreach (var menu in viewRegistry.GetAllMenus())
        {
            MenuItems.Add(new SysMenuItemViewModel(menu, _permissionService, _languageService, Navigate));
        }

        _authService.AuthStateChanged += _ => _dispatcherService.Post(RefreshAuthState);
        _permissionService.PermissionStateChanged += () => _dispatcherService.Post(RefreshMenuPermissions);
        _languageService.LanguageChanged += (_, _) => _dispatcherService.Post(RefreshLocalization);
    }

    public override string ViewId => "Core.SysMenu";

    public override string ViewTitle => _languageService.GetText("Shell_ViewTitle_SysMenu");

    public ObservableCollection<SysMenuItemViewModel> MenuItems { get; } = [];

    [ObservableProperty]
    private SysMenuItemViewModel? selectedItem;

    public string LoginButtonText => _authService.IsAuthenticated
        ? string.Format(
            CultureInfo.CurrentCulture,
            _languageService.GetText("Shell_LogoutFormat"),
            _authService.CurrentUser?.DisplayName ?? _authService.CurrentUser?.EmployeeNo ?? string.Empty)
        : _languageService.GetText("Shell_Login");

    public event Action<string>? NavigationRequested;

    public event Action? LoginRequested;

    public void SelectMenuItem(string? viewId)
    {
        if (string.IsNullOrWhiteSpace(viewId))
        {
            return;
        }

        foreach (var menu in MenuItems)
        {
            menu.IsSelected = string.Equals(menu.ViewId, viewId, StringComparison.OrdinalIgnoreCase);
            if (menu.IsSelected)
            {
                SelectedItem = menu;
            }
        }
    }

    public void RefreshAuthState()
    {
        OnPropertyChanged(nameof(LoginButtonText));
        RefreshMenuPermissions();
    }

    public void RefreshMenuPermissions()
    {
        foreach (var item in MenuItems)
        {
            item.RefreshPermission();
        }
    }

    public void RefreshLocalization()
    {
        foreach (var item in MenuItems)
        {
            item.RefreshTitle();
        }

        OnPropertyChanged(nameof(ViewTitle));
        OnPropertyChanged(nameof(LoginButtonText));
    }

    public void ExecuteLoginAction()
    {
        if (_authService.IsAuthenticated)
        {
            _authService.Logout();
            return;
        }

        LoginRequested?.Invoke();
    }

    [RelayCommand]
    private void Login() => ExecuteLoginAction();

    private void Navigate(string viewId)
    {
        var item = MenuItems.FirstOrDefault(menu => string.Equals(menu.ViewId, viewId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.IsAccessible)
        {
            return;
        }

        SelectMenuItem(viewId);
        _navigationService.NavigateTo(viewId);
        NavigationRequested?.Invoke(viewId);
    }
}
