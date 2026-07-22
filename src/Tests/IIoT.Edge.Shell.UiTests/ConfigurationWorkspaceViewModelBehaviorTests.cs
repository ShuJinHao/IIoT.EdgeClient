using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Presentation.Navigation.Features.Configuration;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class ConfigurationWorkspaceViewModelBehaviorTests
{
    [Fact]
    public void ConfigurationWorkspace_ShouldExposeRegisteredPluginConfigurationTabs()
    {
        var viewModel = CreateViewModel(permissionService: new FakeClientPermissionService(Permissions.HardwareConfig, Permissions.ParamConfig));

        Assert.Collection(
            viewModel.Tabs,
            tab => Assert.Equal("TestPlugin.IOView", tab.ViewId),
            tab => Assert.Equal("TestPlugin.RecipeView", tab.ViewId),
            tab => Assert.Equal("TestPlugin.ParamView", tab.ViewId),
            tab => Assert.Equal("TestPlugin.HardwareConfigView", tab.ViewId),
            tab => Assert.Equal("TestPlugin.PlcTaskBindingView", tab.ViewId));
        Assert.Equal("TestPlugin.IOView", viewModel.SelectedTab?.ViewId);
        Assert.True(viewModel.IsContentVisible);
    }

    [Fact]
    public void ConfigurationWorkspace_ShouldUseMenuRequiredPermission()
    {
        var permissionService = new FakeClientPermissionService();
        var viewModel = CreateViewModel(permissionService: permissionService);

        var hardwareTab = viewModel.Tabs.Single(tab => tab.ViewId == "TestPlugin.HardwareConfigView");
        viewModel.SelectTabCommand.Execute(hardwareTab);

        Assert.Equal(Permissions.HardwareConfig, viewModel.SelectedTab?.RequiredPermission);
        Assert.False(viewModel.HasPermissionForSelectedTab);
        Assert.True(viewModel.IsPermissionBlocked);

        var ioTab = viewModel.Tabs.Single(tab => tab.ViewId == "TestPlugin.IOView");
        viewModel.SelectTabCommand.Execute(ioTab);

        Assert.Equal(string.Empty, viewModel.SelectedTab?.RequiredPermission);
        Assert.True(viewModel.HasPermissionForSelectedTab);
        Assert.True(viewModel.IsContentVisible);
    }

    [Fact]
    public void ConfigurationWorkspace_ShouldRefreshPermissionState()
    {
        var permissionService = new FakeClientPermissionService();
        var viewModel = CreateViewModel(permissionService: permissionService);
        var hardwareTab = viewModel.Tabs.Single(tab => tab.ViewId == "TestPlugin.HardwareConfigView");
        viewModel.SelectTabCommand.Execute(hardwareTab);

        Assert.False(viewModel.HasPermissionForSelectedTab);

        permissionService.Allow(Permissions.HardwareConfig);

        Assert.True(viewModel.HasPermissionForSelectedTab);
        Assert.True(viewModel.IsContentVisible);
    }

    [Fact]
    public void ConfigurationWorkspace_ShouldRefreshLanguageText()
    {
        var languageService = new FakeLanguageService();
        var viewModel = CreateViewModel(
            permissionService: new FakeClientPermissionService(Permissions.HardwareConfig, Permissions.ParamConfig),
            languageService: languageService);

        Assert.Equal("配方", viewModel.Tabs[1].Title);

        languageService.Change(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Recipe", viewModel.Tabs[1].Title);
    }

    [AvaloniaFact]
    public void ConfigurationWorkspaceView_ShouldRefreshVisibleTabTitles()
    {
        var languageService = new FakeLanguageService();
        using var viewModel = CreateViewModel(
            permissionService: new FakeClientPermissionService(Permissions.HardwareConfig, Permissions.ParamConfig),
            languageService: languageService);
        var view = new ConfigurationWorkspaceView
        {
            DataContext = viewModel
        };
        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = view
        };

        try
        {
            window.Show();

            Assert.Contains("配方", GetVisibleTabTitles(view));

            languageService.Change(CultureInfo.GetCultureInfo("en-US"));

            Assert.Contains("Recipe", GetVisibleTabTitles(view));
            Assert.DoesNotContain("配方", GetVisibleTabTitles(view));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ConfigurationWorkspace_ShouldStopWhenMultiplePluginConfigurationGroupsExist()
    {
        var registry = new FakeViewRegistry();
        registry.RegisterMenu(CreateMenu("TestPlugin.IOView", "Navigation_Menu_Io", "IO", 3));
        registry.RegisterMenu(CreateMenu("Other.IOView", "Navigation_Menu_Io", "IO", 3));

        var viewModel = new ConfigurationWorkspaceViewModel(
            registry,
            new FakeClientPermissionService(),
            new FakeLanguageService());

        Assert.Empty(viewModel.Tabs);
        Assert.False(viewModel.HasSelectedTab);
        Assert.Equal("配置插件不唯一", viewModel.EmptyTitle);
    }

    private static ConfigurationWorkspaceViewModel CreateViewModel(
        IClientPermissionService? permissionService = null,
        IAppLanguageService? languageService = null)
    {
        var registry = new FakeViewRegistry();
        registry.RegisterMenu(CreateMenu("TestPlugin.ParamView", "Navigation_Menu_ParamConfig", "参数", 6, Permissions.ParamConfig));
        registry.RegisterMenu(CreateMenu("TestPlugin.IOView", "Navigation_Menu_Io", "IO", 3));
        registry.RegisterMenu(CreateMenu("TestPlugin.PlcTaskBindingView", "Navigation_Menu_PlcTaskBinding", "绑定", 8, Permissions.HardwareConfig));
        registry.RegisterMenu(CreateMenu("TestPlugin.RecipeView", "Navigation_Menu_Recipe", "配方", 5));
        registry.RegisterMenu(CreateMenu("TestPlugin.HardwareConfigView", "Navigation_Menu_HardwareConfig", "硬件", 7, Permissions.HardwareConfig));
        registry.RegisterMenu(CreateMenu("TestPlugin.DataView", "Navigation_Menu_Data", "数据", 1));

        return new ConfigurationWorkspaceViewModel(
            registry,
            permissionService ?? new FakeClientPermissionService(),
            languageService ?? new FakeLanguageService());
    }

    private static string[] GetVisibleTabTitles(ConfigurationWorkspaceView view)
    {
        var tabs = view.FindControl<EdgeSegmentedNav>("ConfigurationTabsHost");
        Assert.NotNull(tabs);

        return tabs
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToArray();
    }

    private static MenuInfo CreateMenu(
        string viewId,
        string titleResourceKey,
        string title,
        int order,
        string requiredPermission = "")
        => new()
        {
            ViewId = viewId,
            TitleResourceKey = titleResourceKey,
            Title = title,
            Order = order,
            RequiredPermission = requiredPermission
        };

    private sealed class FakeViewRegistry : IViewRegistry
    {
        private readonly List<MenuInfo> _menus = [];

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

        public void RegisterMenu(MenuInfo menuInfo) => _menus.Add(menuInfo);

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

        public IReadOnlyList<MenuInfo> GetAllMenus() => _menus;

        public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => [];
    }

    private sealed class FakeClientPermissionService : IClientPermissionService
    {
        private readonly HashSet<string> _allowedPermissions = new(StringComparer.OrdinalIgnoreCase);

        public FakeClientPermissionService(params string[] allowedPermissions)
        {
            foreach (var permission in allowedPermissions)
            {
                _allowedPermissions.Add(permission);
            }
        }

        public bool CanEditParams => HasPermission(Permissions.ParamConfig);

        public bool CanEditHardware => HasPermission(Permissions.HardwareConfig);

        public bool IsLocalAdmin => CanEditParams && CanEditHardware;

        public event Action? PermissionStateChanged;

        public bool HasPermission(string permission)
            => string.IsNullOrWhiteSpace(permission) || _allowedPermissions.Contains(permission);

        public void Allow(string permission)
        {
            _allowedPermissions.Add(permission);
            PermissionStateChanged?.Invoke();
        }
    }

    private sealed class FakeLanguageService : IAppLanguageService
    {
        private readonly Dictionary<string, Dictionary<string, string>> _values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Navigation_Menu_HardwareConfig"] = "硬件",
                ["Navigation_Menu_Io"] = "IO",
                ["Navigation_Menu_Recipe"] = "配方",
                ["Navigation_Menu_ParamConfig"] = "参数",
                ["Navigation_Menu_PlcTaskBinding"] = "绑定",
                ["Navigation_ConfigurationWorkspace_AmbiguousTitle"] = "配置插件不唯一"
            },
            ["en-US"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Navigation_Menu_HardwareConfig"] = "Hardware",
                ["Navigation_Menu_Io"] = "IO",
                ["Navigation_Menu_Recipe"] = "Recipe",
                ["Navigation_Menu_ParamConfig"] = "Params",
                ["Navigation_Menu_PlcTaskBinding"] = "Binding",
                ["Navigation_ConfigurationWorkspace_AmbiguousTitle"] = "Multiple configuration plugins"
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
            => string.Format(Current, GetString(key, fallback), args);
    }
}
