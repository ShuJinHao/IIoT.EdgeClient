using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Presentation.Shell.Features.Header;
using IIoT.Edge.Presentation.Shell.Features.SysMenu;
using IIoT.Edge.Presentation.Shell.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Xunit;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.Shell.Tests;

public sealed class LocalizationBehaviorTests
{
    [Fact]
    public Task AppLanguageService_Change_ReplacesResourceDictionariesAndUpdatesCulture()
        => RunOnStaThreadAsync(() =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var tempFile = CreateLanguageStateFilePath();

            try
            {
                EnsureApplication();
                var service = new AppLanguageService(tempFile);
                var raised = false;
                service.LanguageChanged += (_, _) => raised = true;
                service.Initialize();

                Assert.Equal("IIoT 边缘客户端", WpfApplication.Current.TryFindResource("Shell_SystemTitle"));

                service.Change(CultureInfo.GetCultureInfo("en-US"));

                Assert.True(raised);
                Assert.Equal("en-US", service.Current.Name);
                Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
                Assert.Equal("IIoT Edge Client", WpfApplication.Current.TryFindResource("Shell_SystemTitle"));
                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                RestoreCulture(originalCulture, originalUiCulture);
                TryDeleteDirectory(Path.GetDirectoryName(tempFile));
            }
        });

    [Fact]
    public Task HeaderViewModel_WhenLanguageChanges_ShouldUpdateSelectedLanguageAndDynamicResources()
        => RunOnStaThreadAsync(() =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var service = new FakeLanguageService();

            try
            {
                var viewModel = new HeaderViewModel(service);

                Assert.Equal("zh-CN", viewModel.SelectedLanguage.Name);

                service.Change(CultureInfo.GetCultureInfo("en-US"));

                Assert.Equal("en-US", viewModel.SelectedLanguage.Name);
            }
            finally
            {
                RestoreCulture(originalCulture, originalUiCulture);
            }
        });

    [Fact]
    public Task DataGridColumnHeaders_WhenLanguageChanges_ShouldRefreshExplicitResourceHeaders()
        => RunOnStaThreadAsync(() =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var tempFile = CreateLanguageStateFilePath();
            Window? window = null;

            try
            {
                EnsureApplication();
                var service = new AppLanguageService(tempFile);
                service.Initialize();

                var dataGrid = new DataGrid();
                var column = new DataGridTextColumn { Binding = new Binding("Value") };
                DataGridColumnLocalization.SetHeaderResourceKey(column, "Navigation_Column_CellData");
                dataGrid.Columns.Add(column);
                window = new Window
                {
                    Width = 1,
                    Height = 1,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = dataGrid
                };
                window.Show();
                DataGridColumnLocalization.RefreshColumns(dataGrid);

                Assert.Equal("产品数据", column.Header);

                service.Change(CultureInfo.GetCultureInfo("en-US"));
                Assert.Equal("Cell Data", column.Header);

                service.Change(CultureInfo.GetCultureInfo("zh-CN"));
                Assert.Equal("产品数据", column.Header);
            }
            finally
            {
                window?.Close();
                RestoreCulture(originalCulture, originalUiCulture);
                TryDeleteDirectory(Path.GetDirectoryName(tempFile));
            }
        });

    [Fact]
    public Task SysMenuViewModel_WhenLanguageChanges_ShouldRefreshLoginAndResourceMenus()
        => RunOnStaThreadAsync(() =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var service = new FakeLanguageService();
            var viewRegistry = new FakeViewRegistry(
            [
                new MenuInfo
                {
                    Title = "系统诊断",
                    TitleResourceKey = "Navigation_Menu_CoreDiagnostics",
                    ViewId = "Core.Diagnostics",
                    Icon = "Stethoscope",
                    Order = 1
                },
                new MenuInfo
                {
                    Title = "旧菜单",
                    ViewId = "Legacy.Menu",
                    Icon = "Menu",
                    Order = 2
                }
            ]);

            try
            {
                var viewModel = new SysMenuViewModel(
                    new FakeNavigationService(),
                    new FakeAuthService(isAuthenticated: true),
                    new FakePermissionService(),
                    service,
                    viewRegistry);

                Assert.Equal("注销 (张三)", viewModel.LoginButtonText);
                Assert.Equal("系统诊断", viewModel.MenuItems[0].Title);
                Assert.Equal("旧菜单", viewModel.MenuItems[1].Title);

                service.Change(CultureInfo.GetCultureInfo("en-US"));

                Assert.Equal("Sign out (张三)", viewModel.LoginButtonText);
                Assert.Equal("System Diagnostics", viewModel.MenuItems[0].Title);
                Assert.Equal("旧菜单", viewModel.MenuItems[1].Title);
            }
            finally
            {
                RestoreCulture(originalCulture, originalUiCulture);
            }
        });

    private static string CreateLanguageStateFilePath()
        => Path.Combine(Path.GetTempPath(), "edge-language-tests", Guid.NewGuid().ToString("N"), "language.json");

    private static void EnsureApplication()
        => WpfTestDispatcher.EnsureApplication();

    private static void RestoreCulture(CultureInfo originalCulture, CultureInfo originalUiCulture)
    {
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
        Thread.CurrentThread.CurrentCulture = originalCulture;
        Thread.CurrentThread.CurrentUICulture = originalUiCulture;
    }

    private static void TryDeleteDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static Task RunOnStaThreadAsync(Action testBody)
        => WpfTestDispatcher.RunAsync(testBody);

    private sealed class FakeAuthService(bool isAuthenticated) : IAuthService
    {
        public UserSession? CurrentUser => isAuthenticated
            ? new UserSession { DisplayName = "张三" }
            : null;

        public bool IsAuthenticated => isAuthenticated;

        public event Action<UserSession?>? AuthStateChanged;

        public bool HasPermission(string permission) => true;

        public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(isAuthenticated);

        public Task<AuthResult> LoginLocalAsync(string password)
            => Task.FromResult(AuthResult.Ok("ok"));

        public Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
            => Task.FromResult(AuthResult.Ok("ok"));

        public void Logout()
            => AuthStateChanged?.Invoke(null);
    }

    private sealed class FakePermissionService : IClientPermissionService
    {
        public bool CanEditParams => true;
        public bool CanEditHardware => true;
        public bool IsLocalAdmin => true;
        public event Action? PermissionStateChanged;
        public bool HasPermission(string permission) => true;
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel => null;
        public FrameworkElement? CurrentView => null;
        public event Action<ViewModelBase?>? Navigated;
        public void NavigateTo(string viewId) => Navigated?.Invoke(null);
    }

    private sealed class FakeLanguageService : IAppLanguageService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _values = new(StringComparer.Ordinal)
        {
            ["zh-CN"] = new(StringComparer.Ordinal)
            {
                ["Shell_Login"] = "登录",
                ["Shell_LogoutFormat"] = "注销 ({0})",
                ["Navigation_Menu_CoreDiagnostics"] = "系统诊断"
            },
            ["en-US"] = new(StringComparer.Ordinal)
            {
                ["Shell_Login"] = "Sign in",
                ["Shell_LogoutFormat"] = "Sign out ({0})",
                ["Navigation_Menu_CoreDiagnostics"] = "System Diagnostics"
            }
        };

        public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

        public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == Current.Name);

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
            new(CultureInfo.GetCultureInfo("en-US"), "English")
        ];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void Change(CultureInfo culture)
        {
            Current = culture;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string fallback = "")
            => _values.TryGetValue(Current.Name, out var values) && values.TryGetValue(key, out var value)
                ? value
                : fallback;

        public string Format(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, GetString(key, fallback), args);
    }

    private sealed class FakeViewRegistry(IReadOnlyList<MenuInfo> menus) : IViewRegistry
    {
        public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        {
        }

        public void RegisterRoute(
            string viewId,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, ViewModelBase> viewModelFactory,
            bool cacheView = true)
        {
        }

        public void RegisterMenu(MenuInfo menuInfo)
        {
        }

        public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
        {
        }

        public void RegisterAnchorable(
            AnchorableInfo info,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, ViewModelBase> viewModelFactory,
            bool cacheView = true)
        {
        }

        public ViewRegistration? GetViewRegistration(string viewId) => null;

        public IReadOnlyList<MenuInfo> GetAllMenus() => menus;

        public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => [];
    }
}
