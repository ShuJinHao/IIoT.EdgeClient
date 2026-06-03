using System.Collections.ObjectModel;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public interface IMonitorViewModelTabController
{
    MonitorTabItemViewModel SelectedTab { get; set; }

    bool IsDeviceStatusTabSelected { get; }

    bool IsStateMachineTabSelected { get; }

    void Initialize();

    void Select(MonitorTabItemViewModel? tab);

    void RefreshLanguage();
}

internal sealed class MonitorViewModelTabController(
    ObservableCollection<MonitorTabItemViewModel> tabs,
    IAppLanguageService languageService,
    IMonitorViewModelCallback callback)
    : IMonitorViewModelTabController
{
    private const string DeviceStatusKey = "DeviceStatus";
    private const string StateMachineKey = "StateMachine";

    private MonitorTabItemViewModel _selectedTab = null!;

    public MonitorTabItemViewModel SelectedTab
    {
        get => _selectedTab;
        set => Select(value);
    }

    public bool IsDeviceStatusTabSelected => _selectedTab?.Key == DeviceStatusKey;

    public bool IsStateMachineTabSelected => _selectedTab?.Key == StateMachineKey;

    public void Initialize()
    {
        tabs.Add(new(languageService, DeviceStatusKey, "Navigation_Monitor_Tab_DeviceStatus", "设备状态"));
        tabs.Add(new(languageService, StateMachineKey, "Navigation_Monitor_Tab_StateMachine", "状态机"));
        Select(tabs[0]);
    }

    public void Select(MonitorTabItemViewModel? tab)
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
        callback.NotifyPropertyChanged(nameof(MonitorViewModel.SelectedTab));
        callback.NotifyPropertyChanged(nameof(MonitorViewModel.IsDeviceStatusTabSelected));
        callback.NotifyPropertyChanged(nameof(MonitorViewModel.IsStateMachineTabSelected));
    }

    public void RefreshLanguage()
    {
        foreach (var tab in tabs)
        {
            tab.RefreshLanguage();
        }
    }
}
