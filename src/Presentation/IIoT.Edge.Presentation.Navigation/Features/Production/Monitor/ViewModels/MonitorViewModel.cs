using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Data;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public class MonitorViewModel : NavigationViewModelBase
{
    private readonly IMonitorViewService _monitorViewService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly LocalizedSyncDiagnosticsText _diagnosticsText;

    public ObservableCollection<DeviceTabVm> DeviceTabs { get; } = new();
    public bool HasDeviceTabs => DeviceTabs.Count > 0;
    public bool IsDeviceTabsEmpty => DeviceTabs.Count == 0;

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public MonitorViewModel(IMonitorViewService monitorViewService, IAppLanguageService languageService)
        : this(
            monitorViewService,
            languageService,
            "Production.Monitor",
            "Navigation_Title_RealtimeMonitor",
            "实时监控")
    {
    }

    public MonitorViewModel(
        IMonitorViewService monitorViewService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _monitorViewService = monitorViewService;
        _diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => RunViewTaskInBackground(
            RefreshAsync,
            GetText("Navigation_Monitor_RefreshFailed", "监控刷新失败。"));
    }

    public override async Task OnActivatedAsync()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        await RunViewTaskAsync(
            RefreshAsync,
            GetText("Navigation_Monitor_LoadFailed", "加载监控数据失败。"));
    }

    public override Task OnDeactivatedAsync()
    {
        _refreshTimer.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        var snapshots = await _monitorViewService.GetSnapshotsAsync();

        SyncItemsByKey(
            DeviceTabs,
            snapshots,
            tab => tab.DeviceName,
            snapshot => snapshot.DeviceName,
            snapshot => new DeviceTabVm { DeviceName = snapshot.DeviceName },
            (tab, snapshot) =>
            {
                tab.DayShiftOk = snapshot.DayShiftOk;
                tab.DayShiftNg = snapshot.DayShiftNg;
                tab.DayShiftTotal = snapshot.DayShiftTotal;
                tab.DayShiftYield = snapshot.DayShiftYield;
                tab.NightShiftOk = snapshot.NightShiftOk;
                tab.NightShiftNg = snapshot.NightShiftNg;
                tab.NightShiftTotal = snapshot.NightShiftTotal;
                tab.NightShiftYield = snapshot.NightShiftYield;
                tab.TotalAll = snapshot.TotalAll;
                tab.OkAll = snapshot.OkAll;
                tab.NgAll = snapshot.NgAll;
                tab.YieldAll = snapshot.YieldAll;
                tab.DeviceDataSummary = snapshot.DeviceDataSummary;
                tab.StepSummary = snapshot.StepSummary;
                tab.CloudSyncStatus = _diagnosticsText.FormatCloudMonitorSummary(snapshot.CloudSync);
                tab.MesSyncStatus = _diagnosticsText.FormatMesMonitorSummary(snapshot.MesSync);
                tab.ContextPersistenceStatus = _diagnosticsText.FormatContextPersistenceSummary(snapshot.ContextPersistence);
                tab.CellCount = snapshot.CellCount;
                tab.CellTable = snapshot.CellTable;
            });
        OnPropertyChanged(nameof(HasDeviceTabs));
        OnPropertyChanged(nameof(IsDeviceTabsEmpty));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        foreach (var tab in DeviceTabs)
        {
            tab.RefreshFallbackText(this);
        }
    }
}

public class DeviceTabVm : BaseNotifyPropertyChanged
{
    private string _deviceName = string.Empty;
    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(); }
    }

    private int _dayShiftOk;
    public int DayShiftOk
    {
        get => _dayShiftOk;
        set { _dayShiftOk = value; OnPropertyChanged(); }
    }

    private int _dayShiftNg;
    public int DayShiftNg
    {
        get => _dayShiftNg;
        set { _dayShiftNg = value; OnPropertyChanged(); }
    }

    private int _dayShiftTotal;
    public int DayShiftTotal
    {
        get => _dayShiftTotal;
        set { _dayShiftTotal = value; OnPropertyChanged(); }
    }

    private string _dayShiftYield = "0%";
    public string DayShiftYield
    {
        get => _dayShiftYield;
        set { _dayShiftYield = value; OnPropertyChanged(); }
    }

    private int _nightShiftOk;
    public int NightShiftOk
    {
        get => _nightShiftOk;
        set { _nightShiftOk = value; OnPropertyChanged(); }
    }

    private int _nightShiftNg;
    public int NightShiftNg
    {
        get => _nightShiftNg;
        set { _nightShiftNg = value; OnPropertyChanged(); }
    }

    private int _nightShiftTotal;
    public int NightShiftTotal
    {
        get => _nightShiftTotal;
        set { _nightShiftTotal = value; OnPropertyChanged(); }
    }

    private string _nightShiftYield = "0%";
    public string NightShiftYield
    {
        get => _nightShiftYield;
        set { _nightShiftYield = value; OnPropertyChanged(); }
    }

    private int _totalAll;
    public int TotalAll
    {
        get => _totalAll;
        set { _totalAll = value; OnPropertyChanged(); }
    }

    private int _okAll;
    public int OkAll
    {
        get => _okAll;
        set { _okAll = value; OnPropertyChanged(); }
    }

    private int _ngAll;
    public int NgAll
    {
        get => _ngAll;
        set { _ngAll = value; OnPropertyChanged(); }
    }

    private string _yieldAll = "0%";
    public string YieldAll
    {
        get => _yieldAll;
        set { _yieldAll = value; OnPropertyChanged(); }
    }

    private string _deviceDataSummary = string.Empty;
    public string DeviceDataSummary
    {
        get => _deviceDataSummary;
        set { _deviceDataSummary = value; OnPropertyChanged(); }
    }

    private string _stepSummary = string.Empty;
    public string StepSummary
    {
        get => _stepSummary;
        set { _stepSummary = value; OnPropertyChanged(); }
    }

    private string _cloudSyncStatus = string.Empty;
    public string CloudSyncStatus
    {
        get => _cloudSyncStatus;
        set { _cloudSyncStatus = value; OnPropertyChanged(); }
    }

    private string _mesSyncStatus = string.Empty;
    public string MesSyncStatus
    {
        get => _mesSyncStatus;
        set { _mesSyncStatus = value; OnPropertyChanged(); }
    }

    private string _contextPersistenceStatus = string.Empty;
    public string ContextPersistenceStatus
    {
        get => _contextPersistenceStatus;
        set { _contextPersistenceStatus = value; OnPropertyChanged(); }
    }

    private int _cellCount;
    public int CellCount
    {
        get => _cellCount;
        set { _cellCount = value; OnPropertyChanged(); }
    }

    private DataTable? _cellTable;
    public DataTable? CellTable
    {
        get => _cellTable;
        set
        {
            _cellTable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CellRows));
        }
    }

    public System.Data.DataView? CellRows => _cellTable?.DefaultView;

    public void RefreshFallbackText(MonitorViewModel owner)
    {
        if (string.IsNullOrWhiteSpace(DeviceDataSummary))
        {
            DeviceDataSummary = owner.GetText("Navigation_Monitor_NoDeviceData", "暂无数据");
        }

        if (string.IsNullOrWhiteSpace(StepSummary))
        {
            StepSummary = owner.GetText("Navigation_Monitor_NoTaskStep", "暂无步骤");
        }

        if (string.IsNullOrWhiteSpace(CloudSyncStatus))
        {
            CloudSyncStatus = owner.GetText("Navigation_Monitor_CloudUnknown", "云端状态未知");
        }

        if (string.IsNullOrWhiteSpace(MesSyncStatus))
        {
            MesSyncStatus = owner.GetText("Navigation_Monitor_MesUnknown", "MES 状态未知");
        }

        if (string.IsNullOrWhiteSpace(ContextPersistenceStatus))
        {
            ContextPersistenceStatus = owner.GetText("Navigation_Monitor_ContextPersistenceOk", "损坏文件数：0");
        }
    }
}
