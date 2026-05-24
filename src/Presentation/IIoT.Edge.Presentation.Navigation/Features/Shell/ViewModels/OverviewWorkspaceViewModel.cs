using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public sealed class OverviewWorkspaceViewModel : BaseNotifyPropertyChanged
{
    public const string TodayOverviewViewId = "Overview.Today";

    private const string DataViewSuffix = ".DataView";
    private const string CapacityViewSuffix = ".CapacityView";
    private const string StandardCapacityViewId = "Production.CapacityView";

    private readonly IViewRegistry _viewRegistry;
    private readonly IAppLanguageService _languageService;
    private OverviewTabItemViewModel _selectedTab = null!;

    public OverviewWorkspaceViewModel(IViewRegistry viewRegistry, IAppLanguageService languageService)
    {
        _viewRegistry = viewRegistry;
        _languageService = languageService;
        Tabs =
        [
            new(_languageService, "Overview.Today", TodayOverviewViewId, "Navigation_Overview_Tab_Today", "今日总览"),
            new(_languageService, "Overview.ProductionData", ResolvePluginRoute(DataViewSuffix), "Navigation_Overview_Tab_ProductionData", "生产数据"),
            new(_languageService, "Overview.Capacity", ResolvePluginRoute(CapacityViewSuffix) ?? StandardCapacityViewId, "Navigation_Overview_Tab_Capacity", "产能查询")
        ];

        SelectTabCommand = new BaseCommand(parameter =>
        {
            if (parameter is OverviewTabItemViewModel tab)
            {
                Select(tab);
            }
        });
        Select(Tabs[0]);

        _languageService.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public ObservableCollection<OverviewTabItemViewModel> Tabs { get; }

    public OverviewTabItemViewModel SelectedTab
    {
        get => _selectedTab;
        set => Select(value);
    }

    public ICommand SelectTabCommand { get; }

    public string MissingDataTitle => _languageService.GetString("Navigation_Overview_MissingDataTitle", "生产数据未注册");

    public string MissingDataDescription => _languageService.GetString(
        "Navigation_Overview_MissingDataDescription",
        "当前未找到已启用插件注册的生产数据页面。");

    public string MissingCapacityTitle => _languageService.GetString("Navigation_Overview_MissingCapacityTitle", "产能查询未注册");

    public string MissingCapacityDescription => _languageService.GetString(
        "Navigation_Overview_MissingCapacityDescription",
        "当前未找到可用的产能查询页面。");

    private void Select(OverviewTabItemViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SetSelectedTab(tab);
    }

    private void SetSelectedTab(OverviewTabItemViewModel tab)
    {
        if (ReferenceEquals(_selectedTab, tab))
        {
            return;
        }

        _selectedTab = tab;
        OnPropertyChanged(nameof(SelectedTab));
    }

    private void RefreshLanguage()
    {
        var selectedTab = SelectedTab;
        var tabs = Tabs.ToArray();

        foreach (var tab in Tabs)
        {
            tab.RefreshLanguage();
        }

        Tabs.Clear();
        foreach (var tab in tabs)
        {
            Tabs.Add(tab);
        }

        Select(selectedTab);

        OnPropertyChanged(nameof(MissingDataTitle));
        OnPropertyChanged(nameof(MissingDataDescription));
        OnPropertyChanged(nameof(MissingCapacityTitle));
        OnPropertyChanged(nameof(MissingCapacityDescription));
        OnPropertyChanged(nameof(SelectedTab));
    }

    private string? ResolvePluginRoute(string suffix)
    {
        return _viewRegistry
            .GetAllMenus()
            .Where(menu => IsPluginRoute(menu.ViewId, suffix))
            .OrderBy(menu => menu.Order)
            .Select(menu => menu.ViewId)
            .FirstOrDefault();
    }

    private static bool IsPluginRoute(string viewId, string suffix)
    {
        return viewId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && !viewId.StartsWith("Production.", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class OverviewTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public OverviewTabItemViewModel(
        IAppLanguageService languageService,
        string key,
        string? viewId,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        Key = key;
        ViewId = viewId;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
    }

    public string Key { get; }

    public string? ViewId { get; }

    public string TitleResourceKey { get; }

    public string TitleFallback { get; }

    public string Title => _languageService.GetString(TitleResourceKey, TitleFallback);

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

    public override string ToString()
    {
        return Title;
    }
}
