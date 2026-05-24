using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;

public class CapacityViewModel : NavigationViewModelBase
{
    private readonly ICapacityViewService _capacityViewService;
    private string _selectedDeviceName = string.Empty;
    private string _selectedQueryMode = CapacityQueryModes.Day;
    private CapacityQueryModeOption? _selectedQueryModeOption;
    private DateTime _queryDate = DateTime.Today;
    private bool _isOnline;
    private int _periodTotal;
    private int _periodOk;
    private int _periodNg;
    private string _periodYield = "0%";
    private string _avgDaily = "0";

    public ObservableCollection<string> DeviceNames { get; } = [];
    public ObservableCollection<CapacityQueryModeOption> QueryModes { get; } = [];
    public ObservableCollection<DailyCapacityVm> DailyRecords { get; } = [];
    public ObservableCollection<CapacityChartBarVm> ChartBars { get; } = [];
    public bool HasDailyRecords => DailyRecords.Count > 0;
    public bool IsDailyRecordsEmpty => DailyRecords.Count == 0;

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
            SetSelectedQueryMode(value, true);
        }
    }

    public CapacityQueryModeOption? SelectedQueryModeOption
    {
        get => _selectedQueryModeOption;
        set
        {
            if (ReferenceEquals(_selectedQueryModeOption, value))
            {
                return;
            }

            _selectedQueryModeOption = value;
            OnPropertyChanged();
            if (value is not null)
            {
                SetSelectedQueryMode(value.Value, false);
            }
        }
    }

    public DateTime QueryDate
    {
        get => _queryDate;
        set
        {
            _queryDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QueryDateOffset));
        }
    }

    public DateTimeOffset? QueryDateOffset
    {
        get => new(_queryDate.Date);
        set
        {
            if (value is null)
            {
                return;
            }

            QueryDate = value.Value.Date;
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

    public CapacityViewModel(ICapacityViewService capacityViewService, IAppLanguageService languageService)
        : this(
            capacityViewService,
            languageService,
            "Production.CapacityView",
            "Navigation_Title_CapacityQuery",
            "产能查询")
    {
    }

    public CapacityViewModel(
        ICapacityViewService capacityViewService,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _capacityViewService = capacityViewService;

        QueryCommand = new AsyncCommand(() => RunViewTaskAsync(QueryHistoryAsync, GetText("Navigation_Capacity_QueryFailed", "产能查询失败。")));
        ExportCommand = new BaseCommand(_ => { });
        RefreshQueryModes();
        SetSelectedQueryMode(_selectedQueryMode, true);

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
            SetDailyRecords(Array.Empty<DailyCapacityVm>());
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
            SetStatus(GetText("Navigation_Capacity_OfflineHint", "设备上传授权尚未就绪，暂时无法查询云端产能。"));
            ClearSummary();
            SetDailyRecords(Array.Empty<DailyCapacityVm>());
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
        SetDailyRecords(result.Rows);
        PeriodTotal = result.PeriodTotal;
        PeriodOk = result.PeriodOk;
        PeriodNg = result.PeriodNg;
        PeriodYield = result.PeriodYield;
        AvgDaily = result.AvgDaily;
        RefreshChart();
    }

    private void SetDailyRecords(IEnumerable<DailyCapacityVm> records)
    {
        ReplaceItems(DailyRecords, records);
        OnPropertyChanged(nameof(HasDailyRecords));
        OnPropertyChanged(nameof(IsDailyRecordsEmpty));
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

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        RefreshQueryModes();
        SetSelectedQueryMode(_selectedQueryMode, true);
    }

    private void RefreshQueryModes()
    {
        ReplaceItems(
            QueryModes,
            [
                new(CapacityQueryModes.Day, GetText("Navigation_Capacity_ByDay", "按日查询")),
                new(CapacityQueryModes.Month, GetText("Navigation_Capacity_ByMonth", "按月查询")),
                new(CapacityQueryModes.Year, GetText("Navigation_Capacity_ByYear", "按年查询"))
            ]);
    }

    private void SetSelectedQueryMode(string value, bool updateOption)
    {
        if (_selectedQueryMode != value)
        {
            _selectedQueryMode = value;
            OnPropertyChanged(nameof(SelectedQueryMode));
        }

        if (!updateOption)
        {
            return;
        }

        var option = QueryModes.FirstOrDefault(x => x.Value == _selectedQueryMode);
        if (ReferenceEquals(_selectedQueryModeOption, option))
        {
            return;
        }

        _selectedQueryModeOption = option;
        OnPropertyChanged(nameof(SelectedQueryModeOption));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }
}

public sealed class CapacityQueryModeOption(string value, string displayName)
{
    public string Value { get; } = value;
    public string DisplayName { get; } = displayName;
}
