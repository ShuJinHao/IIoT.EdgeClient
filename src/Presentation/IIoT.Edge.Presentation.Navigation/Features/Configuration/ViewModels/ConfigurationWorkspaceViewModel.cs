using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Configuration;

public sealed class ConfigurationWorkspaceViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private static readonly string[] RouteSuffixes =
    [
        ".IOView",
        ".RecipeView",
        ".ParamView",
        ".HardwareConfigView",
        ".PlcTaskBindingView"
    ];

    private readonly IViewRegistry _viewRegistry;
    private readonly IClientPermissionService _permissionService;
    private readonly IAppLanguageService _languageService;
    private bool _hasAmbiguousModules;
    private ConfigurationWorkspaceTabItemViewModel? _selectedTab;

    public ConfigurationWorkspaceViewModel(
        IViewRegistry viewRegistry,
        IClientPermissionService permissionService,
        IAppLanguageService languageService)
    {
        _viewRegistry = viewRegistry;
        _permissionService = permissionService;
        _languageService = languageService;

        SelectTabCommand = new BaseCommand(parameter =>
        {
            if (parameter is ConfigurationWorkspaceTabItemViewModel tab)
            {
                Select(tab);
            }
        });

        RefreshTabs();
        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
        _languageService.LanguageChanged += HandleLanguageChanged;
    }

    public ObservableCollection<ConfigurationWorkspaceTabItemViewModel> Tabs { get; } = [];

    public ConfigurationWorkspaceTabItemViewModel? SelectedTab
    {
        get => _selectedTab;
        set => Select(value);
    }

    public ICommand SelectTabCommand { get; }

    public bool HasTabs => Tabs.Count > 0;

    public bool HasSelectedTab => SelectedTab is not null;

    public bool HasPermissionForSelectedTab => SelectedTab?.HasPermission ?? false;

    public bool IsPermissionBlocked => HasSelectedTab && !HasPermissionForSelectedTab;

    public bool IsContentVisible => HasSelectedTab && HasPermissionForSelectedTab;

    public string EmptyTitle => _hasAmbiguousModules
        ? _languageService.GetString("Navigation_ConfigurationWorkspace_AmbiguousTitle", "配置插件不唯一")
        : _languageService.GetString("Navigation_ConfigurationWorkspace_EmptyTitle", "暂无配置页");

    public string EmptyMessage => _hasAmbiguousModules
        ? _languageService.GetString("Navigation_ConfigurationWorkspace_AmbiguousMessage", "当前发现多个插件配置页，请先确认配置入口选择规则。")
        : _languageService.GetString("Navigation_ConfigurationWorkspace_EmptyMessage", "当前没有插件注册可用配置页面。");

    public string NoPermissionTitle => _languageService.GetString(
        "Navigation_ConfigurationWorkspace_NoPermissionTitle",
        "需要管理员权限");

    public string NoPermissionMessage => _languageService.GetString(
        "Navigation_ConfigurationWorkspace_NoPermissionMessage",
        "当前账号没有访问此配置页的权限，请登录管理员账号后再试。");

    public void Dispose()
    {
        _permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
        _languageService.LanguageChanged -= HandleLanguageChanged;
    }

    private void RefreshTabs()
    {
        var selectedViewId = SelectedTab?.ViewId;
        var candidates = ResolveConfigurationMenus();

        Tabs.Clear();
        foreach (var candidate in candidates)
        {
            Tabs.Add(new ConfigurationWorkspaceTabItemViewModel(candidate.Menu, _permissionService, _languageService));
        }

        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));

        var selected = string.IsNullOrWhiteSpace(selectedViewId)
            ? Tabs.FirstOrDefault()
            : Tabs.FirstOrDefault(tab => string.Equals(tab.ViewId, selectedViewId, StringComparison.OrdinalIgnoreCase))
                ?? Tabs.FirstOrDefault();

        SetSelectedTab(null);
        Select(selected);
    }

    private IReadOnlyList<ConfigurationRouteCandidate> ResolveConfigurationMenus()
    {
        _hasAmbiguousModules = false;

        var candidates = _viewRegistry
            .GetAllMenus()
            .Select(TryCreateCandidate)
            .OfType<ConfigurationRouteCandidate>()
            .ToArray();

        var moduleIds = candidates
            .Select(candidate => candidate.ModuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (moduleIds.Length > 1)
        {
            _hasAmbiguousModules = true;
            return [];
        }

        return candidates
            .OrderBy(candidate => candidate.Menu.Order)
            .ThenBy(candidate => candidate.Menu.ViewId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ConfigurationRouteCandidate? TryCreateCandidate(MenuInfo menu)
    {
        foreach (var suffix in RouteSuffixes)
        {
            if (!menu.ViewId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var moduleId = menu.ViewId[..^suffix.Length].TrimEnd('.');
            if (string.IsNullOrWhiteSpace(moduleId) || !menu.ViewId.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

            return new ConfigurationRouteCandidate(menu, moduleId);
        }

        return null;
    }

    private void Select(ConfigurationWorkspaceTabItemViewModel? tab)
    {
        if (tab is null)
        {
            foreach (var item in Tabs)
            {
                item.IsSelected = false;
            }

            SetSelectedTab(null);
            return;
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SetSelectedTab(tab);
    }

    private void SetSelectedTab(ConfigurationWorkspaceTabItemViewModel? tab)
    {
        if (ReferenceEquals(_selectedTab, tab))
        {
            return;
        }

        _selectedTab = tab;
        OnPropertyChanged(nameof(SelectedTab));
        OnSelectionStateChanged();
    }

    private void HandlePermissionStateChanged()
        => RunOnUiThread(() =>
        {
            foreach (var tab in Tabs)
            {
                tab.RefreshPermission();
            }

            OnSelectionStateChanged();
        });

    private void HandleLanguageChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            foreach (var tab in Tabs)
            {
                tab.RefreshLanguage();
            }

            OnPropertyChanged(nameof(EmptyTitle));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(NoPermissionTitle));
            OnPropertyChanged(nameof(NoPermissionMessage));
            OnPropertyChanged(nameof(SelectedTab));
        });

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void OnSelectionStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTab));
        OnPropertyChanged(nameof(HasPermissionForSelectedTab));
        OnPropertyChanged(nameof(IsPermissionBlocked));
        OnPropertyChanged(nameof(IsContentVisible));
    }

    private sealed record ConfigurationRouteCandidate(MenuInfo Menu, string ModuleId);
}

public sealed class ConfigurationWorkspaceTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly MenuInfo _menu;
    private readonly IClientPermissionService _permissionService;
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public ConfigurationWorkspaceTabItemViewModel(
        MenuInfo menu,
        IClientPermissionService permissionService,
        IAppLanguageService languageService)
    {
        _menu = menu;
        _permissionService = permissionService;
        _languageService = languageService;
    }

    public string ViewId => _menu.ViewId;

    public string Title => string.IsNullOrWhiteSpace(_menu.TitleResourceKey)
        ? _menu.Title
        : _languageService.GetString(_menu.TitleResourceKey, _menu.Title);

    public string RequiredPermission => _menu.RequiredPermission;

    public bool HasPermission => string.IsNullOrWhiteSpace(RequiredPermission)
        || _permissionService.HasPermission(RequiredPermission);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
    }

    public void RefreshPermission()
    {
        OnPropertyChanged(nameof(HasPermission));
    }

    public override string ToString()
    {
        return Title;
    }

}
