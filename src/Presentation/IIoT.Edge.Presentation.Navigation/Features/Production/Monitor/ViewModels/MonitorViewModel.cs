using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public class MonitorViewModel : PresentationViewModelBase
{
    public override string ViewId => InjectionViewIds.Monitor;
    public override string ViewTitle => "Real-time Monitor";

    private readonly IMonitorViewService _monitorViewService;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<DeviceTabVm> DeviceTabs { get; } = new();

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set { _selectedTabIndex = value; OnPropertyChanged(); }
    }

    public MonitorViewModel(IMonitorViewService monitorViewService)
    {
        _monitorViewService = monitorViewService;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _refreshTimer.Tick += (_, _) => RunViewTaskInBackground(RefreshAsync, "Monitor refresh failed");
        _refreshTimer.Start();
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(RefreshAsync, "Monitor data load failed");
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
                tab.CloudLinkStatus = snapshot.CloudLinkStatus;
                tab.RetryQueueStatus = snapshot.RetryQueueStatus;
                tab.CellCount = snapshot.CellCount;
                tab.CellTable = snapshot.CellTable;
            });
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

    private string _deviceDataSummary = "No data";
    public string DeviceDataSummary
    {
        get => _deviceDataSummary;
        set { _deviceDataSummary = value; OnPropertyChanged(); }
    }

    private string _stepSummary = "No steps";
    public string StepSummary
    {
        get => _stepSummary;
        set { _stepSummary = value; OnPropertyChanged(); }
    }

    private string _cloudLinkStatus = "Cloud status unknown";
    public string CloudLinkStatus
    {
        get => _cloudLinkStatus;
        set { _cloudLinkStatus = value; OnPropertyChanged(); }
    }

    private string _retryQueueStatus = "Retry queue unknown";
    public string RetryQueueStatus
    {
        get => _retryQueueStatus;
        set { _retryQueueStatus = value; OnPropertyChanged(); }
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
        set { _cellTable = value; OnPropertyChanged(); }
    }
}
