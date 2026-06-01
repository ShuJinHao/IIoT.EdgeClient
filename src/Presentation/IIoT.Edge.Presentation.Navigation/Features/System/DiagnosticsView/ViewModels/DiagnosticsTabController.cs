using System.Collections.ObjectModel;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsTabController
{
    DiagnosticsTabItemViewModel SelectedTab { get; set; }

    bool IsOverviewTabSelected { get; }

    bool IsSyncOpsTabSelected { get; }

    bool IsStartupTabSelected { get; }

    void Initialize();

    void Select(DiagnosticsTabItemViewModel? tab);

    void RefreshLanguage();
}

internal sealed class DiagnosticsTabController(
    ObservableCollection<DiagnosticsTabItemViewModel> tabs,
    IAppLanguageService languageService,
    IDiagnosticsViewModelCallback callback)
    : IDiagnosticsTabController
{
    private const string OverviewKey = "Diag.Overview";
    private const string SyncOpsKey = "Diag.SyncOps";
    private const string StartupKey = "Diag.Startup";

    private DiagnosticsTabItemViewModel _selectedTab = null!;

    public DiagnosticsTabItemViewModel SelectedTab
    {
        get => _selectedTab;
        set => Select(value);
    }

    public bool IsOverviewTabSelected => _selectedTab?.Key == OverviewKey;
    public bool IsSyncOpsTabSelected => _selectedTab?.Key == SyncOpsKey;
    public bool IsStartupTabSelected => _selectedTab?.Key == StartupKey;

    public void Initialize()
    {
        tabs.Add(new(languageService, OverviewKey, "Navigation_Diagnostics_TabOverview", "系统概况"));
        tabs.Add(new(languageService, SyncOpsKey, "Navigation_Diagnostics_TabSyncOps", "同步运维"));
        tabs.Add(new(languageService, StartupKey, "Navigation_Diagnostics_TabStartup", "启动诊断"));
        Select(tabs[0]);
    }

    public void Select(DiagnosticsTabItemViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        foreach (var item in tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        if (ReferenceEquals(_selectedTab, tab))
        {
            return;
        }

        _selectedTab = tab;
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.SelectedTab));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.IsOverviewTabSelected));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.IsSyncOpsTabSelected));
        callback.NotifyPropertyChanged(nameof(DiagnosticsViewModel.IsStartupTabSelected));
    }

    public void RefreshLanguage()
    {
        foreach (var tab in tabs)
        {
            tab.RefreshLanguage();
        }
    }
}

public sealed class DiagnosticsTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public DiagnosticsTabItemViewModel(
        IAppLanguageService languageService,
        string key,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        Key = key;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
    }

    public string Key { get; }

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
        => OnPropertyChanged(nameof(Title));

    public override string ToString()
        => Title;
}
