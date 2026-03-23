using IIoT.Edge.Common.Mvvm;
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Auth;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace IIoT.Edge.UI.Shared.Widgets.SysMenu
{
    public class SysMenuWidget : WidgetBase
    {
        public override string WidgetId => "Core.SysMenu";
        public override string WidgetName => "系统导航菜单";

        private readonly INavigationService _navigationService;
        private readonly IAuthService _authService;

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

        public SysMenuWidget(
            INavigationService navigationService,
            IAuthService authService,
            IViewRegistry viewRegistry)
        {
            _navigationService = navigationService;
            _authService = authService;

            LayoutRow = 1;
            LayoutColumn = 0;

            NavigateCommand = new BaseCommand(ExecuteNavigate);
            LoginCommand = new BaseCommand(_ => ExecuteLogin());

            _authService.AuthStateChanged += _ =>
            {
                // 云端登录是异步回调，必须切回 UI 线程再刷新
                Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshMenuPermissions();
                    OnPropertyChanged(nameof(LoginButtonText));
                });
            };

            BuildMenuItemsFromRegistry(viewRegistry);
        }

        private void BuildMenuItemsFromRegistry(IViewRegistry viewRegistry)
        {
            MenuItems.Clear();
            foreach (var menu in viewRegistry.GetAllMenus().OrderBy(m => m.Order))
                MenuItems.Add(new MenuItemViewModel(menu, _authService));
        }

        private void RefreshMenuPermissions()
        {
            foreach (var item in MenuItems)
                item.RefreshPermission();
        }

        private void ExecuteNavigate(object? parameter)
        {
            if (parameter is not string widgetId) return;
            if (string.IsNullOrEmpty(widgetId)) return;

            var menuItem = MenuItems.FirstOrDefault(m => m.WidgetId == widgetId);
            if (menuItem is null || !menuItem.IsAccessible) return;

            if (SelectedItem is not null)
                SelectedItem.IsSelected = false;

            SelectedItem = menuItem;
            SelectedItem.IsSelected = true;

            _navigationService.NavigateTo(widgetId);
        }

        private void ExecuteLogin()
        {
            if (_authService.IsAuthenticated)
                _authService.Logout();
            else
                DialogHost.OpenDialogCommand.Execute(null, null);
        }
    }
}