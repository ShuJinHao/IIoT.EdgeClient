using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;

public class CapacityViewModel : PresentationViewModelBase
{
    private readonly ICapacityViewService _capacityViewService;
    private readonly string _viewId;
    private readonly string _viewTitle;
    private string _selectedDeviceName = string.Empty;
    private string _selectedQueryMode = CapacityQueryModes.Day;
    private DateTime _queryDate = DateTime.Today;
    private bool _isOnline;
    private int _periodTotal;
    private int _periodOk;
    private int _periodNg;
    private string _periodYield = "0%";
    private string _avgDaily = "0";

    public override string ViewId => _viewId;
    public override string ViewTitle => _viewTitle;

    public ObservableCollection<string> DeviceNames { get; } = [];
    public ObservableCollection<DailyCapacityVm> DailyRecords { get; } = [];
    public ObservableCollection<CapacityChartBarVm> ChartBars { get; } = [];

    public string SelectedDeviceName
    {
        get => _selectedDeviceName;
        set
        {
            _selectedDeviceName = value;
            OnPropertyChanged();
            ScheduleLoadCurrentData();
        }
    }

    public string SelectedQueryMode
    {
        get => _selectedQueryMode;
        set
        {
            _selectedQueryMode = value;
            OnPropertyChanged();
        }
    }

    public DateTime QueryDate
    {
        get => _queryDate;
        set
        {
            _queryDate = value;
            OnPropertyChanged();
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanQueryCloud));
        }
    }

    public bool CanQueryCloud => IsOnline;

    public int PeriodTotal
    {
        get => _periodTotal;
        set
        {
            _periodTotal = value;
            OnPropertyChanged();
        }
    }

    public int PeriodOk
    {
        get => _periodOk;
        set
        {
            _periodOk = value;
            OnPropertyChanged();
        }
    }

    public int PeriodNg
    {
        get => _periodNg;
        set
        {
            _periodNg = value;
            OnPropertyChanged();
        }
    }

    public string PeriodYield
    {
        get => _periodYield;
        set
        {
            _periodYield = value;
            OnPropertyChanged();
        }
    }

    public string AvgDaily
    {
        get => _avgDaily;
        set
        {
            _avgDaily = value;
            OnPropertyChanged();
        }
    }

    public ICommand QueryCommand { get; }
    public ICommand ExportCommand { get; }

    public CapacityViewModel(ICapacityViewService capacityViewService)
        : this(capacityViewService, "Production.CapacityView", string.Empty)
    {
    }

    public CapacityViewModel(
        ICapacityViewService capacityViewService,
        string viewId,
        string viewTitle)
    {
        _capacityViewService = capacityViewService;
        _viewId = viewId;
        _viewTitle = viewTitle;

        QueryCommand = new AsyncCommand(() => RunViewTaskAsync(QueryHistoryAsync, GetText("Navigation_Capacity_QueryFailed", "产能查询失败。")));
        ExportCommand = new BaseCommand(_ => { });

        _capacityViewService.UploadGateChanged += OnUploadGateChanged;
    }

    public override async Task OnActivatedAsync()
    {
        RefreshDeviceList();
        IsOnline = _capacityViewService.IsOnline;
        await RunViewTaskAsync(LoadCurrentDataAsync, GetText("Navigation_Capacity_LoadFailed", "加载产能数据失败。"));
    }

    public void OnCapacityUpdated() => ScheduleLoadCurrentData();

    private void OnUploadGateChanged(EdgeUploadGateSnapshot snapshot)
        => RunOnUiThread(() =>
        {
            IsOnline = snapshot.State == EdgeUploadGateState.Ready;
            RunViewTaskInBackground(LoadCurrentDataAsync, GetText("Navigation_Capacity_LoadFailed", "加载产能数据失败。"));
        });

    private void ScheduleLoadCurrentData()
        => RunOnUiThread(() => RunViewTaskInBackground(LoadCurrentDataAsync, GetText("Navigation_Capacity_LoadFailed", "加载产能数据失败。")));

    private void RefreshDeviceList()
    {
        var names = _capacityViewService.GetDeviceNames();
        ReplaceItems(DeviceNames, names);

        if (!string.IsNullOrEmpty(_selectedDeviceName) && names.Contains(_selectedDeviceName))
        {
            return;
        }

        _selectedDeviceName = names.FirstOrDefault() ?? string.Empty;
        OnPropertyChanged(nameof(SelectedDeviceName));
    }

    private async Task LoadCurrentDataAsync()
    {
        if (!CanQueryCloud)
        {
            ReplaceItems(DailyRecords, Array.Empty<DailyCapacityVm>());
            ClearSummary();
            RefreshChart();
            return;
        }

        var result = await _capacityViewService.LoadTodayAsync(_selectedDeviceName);
        ApplyResult(result);
    }

    private async Task QueryHistoryAsync()
    {
        if (!CanQueryCloud)
        {
            MessageBox.Show(
                GetText("Navigation_Capacity_OfflineHint", "设备上传鉴权尚未就绪，暂时无法查询云端产能。"),
                GetText("Navigation_Title_CapacityQuery", "产能查询"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ClearSummary();
            ReplaceItems(DailyRecords, Array.Empty<DailyCapacityVm>());
            RefreshChart();
            return;
        }

        var result = await _capacityViewService.QueryHistoryAsync(
            SelectedQueryMode,
            QueryDate,
            _selectedDeviceName);

        ApplyResult(result);
    }

    private void ApplyResult(CapacityViewResult result)
    {
        ReplaceItems(DailyRecords, result.Rows);
        PeriodTotal = result.PeriodTotal;
        PeriodOk = result.PeriodOk;
        PeriodNg = result.PeriodNg;
        PeriodYield = result.PeriodYield;
        AvgDaily = result.AvgDaily;
        RefreshChart();
    }

    private void RefreshChart()
    {
        ChartBars.Clear();
        var max = DailyRecords.Count > 0 ? DailyRecords.Max(x => x.Total) : 0;
        var safeMax = max <= 0 ? 1 : max;

        foreach (var row in DailyRecords)
        {
            var ratio = row.Total * 1.0 / safeMax;
            ChartBars.Add(new CapacityChartBarVm
            {
                Label = row.DateFull,
                Value = row.Total,
                HeightRatio = ratio,
                ChartHeight = Math.Max(2, ratio * 190)
            });
        }
    }

    private void ClearSummary()
    {
        PeriodTotal = 0;
        PeriodOk = 0;
        PeriodNg = 0;
        PeriodYield = "0%";
        AvgDaily = "0";
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = dispatcher.InvokeAsync(action);
    }

    private static string GetText(string key, string fallback)
    {
        var value = System.Windows.Application.Current?.TryFindResource(key) as string;
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
