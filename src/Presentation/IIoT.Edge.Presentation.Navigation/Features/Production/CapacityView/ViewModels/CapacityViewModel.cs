using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Media;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;

public class CapacityViewModel : NavigationViewModelBase
{
    private const string ChartTotalKey = "total";
    private const string ChartGoodKey = "good";
    private const string ChartBadKey = "bad";
    private const string ChartYieldKey = "yield";

    private readonly ICapacityQueryFacade _capacityQueryFacade;
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
    public ObservableCollection<DailyCapacitySnapshot> DailyRecords { get; } = [];
    public ObservableCollection<EdgeChartPoint> ChartPoints { get; } = [];
    public ObservableCollection<EdgeChartSeries> ChartSeries { get; } = [];
    public bool HasDailyRecords => DailyRecords.Count > 0;
    public bool IsDailyRecordsEmpty => DailyRecords.Count == 0;
    public bool HasChartRecords => ChartPoints.Count > 0;

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

    public CapacityViewModel(ICapacityQueryFacade capacityQueryFacade, IAppLanguageService languageService)
        : this(
            capacityQueryFacade,
            languageService,
            "Production.CapacityView",
            "Navigation_Title_CapacityQuery",
            "产能查询")
    {
    }

    public CapacityViewModel(
        ICapacityQueryFacade capacityQueryFacade,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _capacityQueryFacade = capacityQueryFacade;

        QueryCommand = new AsyncCommand(() => RunViewTaskAsync(QueryHistoryAsync, GetText("Navigation_Capacity_QueryFailed", "产能查询失败。")));
        ExportCommand = new BaseCommand(_ => { });
        RefreshQueryModes();
        RefreshChartSeries();
        SetSelectedQueryMode(_selectedQueryMode, true);

        _capacityQueryFacade.UploadGateChanged += OnUploadGateChanged;
    }

    public override async Task OnActivatedAsync()
    {
        RefreshDeviceList();
        IsOnline = _capacityQueryFacade.IsOnline;
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
        var names = _capacityQueryFacade.GetDeviceNames();
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
            SetDailyRecords(Array.Empty<DailyCapacitySnapshot>());
            ClearSummary();
            RefreshChart();
            return;
        }

        var result = await _capacityQueryFacade.LoadTodayAsync(_selectedDeviceName);
        ApplyResult(result);
    }

    private async Task QueryHistoryAsync()
    {
        if (!CanQueryCloud)
        {
            SetStatus(GetText("Navigation_Capacity_OfflineHint", "设备上传授权尚未就绪，暂时无法查询云端产能。"));
            ClearSummary();
            SetDailyRecords(Array.Empty<DailyCapacitySnapshot>());
            RefreshChart();
            return;
        }

        var result = await _capacityQueryFacade.QueryHistoryAsync(
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

    private void SetDailyRecords(IEnumerable<DailyCapacitySnapshot> records)
    {
        ReplaceItems(DailyRecords, records);
        OnPropertyChanged(nameof(HasDailyRecords));
        OnPropertyChanged(nameof(IsDailyRecordsEmpty));
    }

    private void RefreshChart()
    {
        ChartPoints.Clear();

        foreach (var row in DailyRecords)
        {
            ChartPoints.Add(new EdgeChartPoint
            {
                Label = string.IsNullOrWhiteSpace(row.Date) ? row.DateFull : row.Date,
                Values = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [ChartTotalKey] = row.Total,
                    [ChartGoodKey] = row.OkCount,
                    [ChartBadKey] = row.NgCount,
                    [ChartYieldKey] = CalculateYieldPercent(row)
                }
            });
        }

        OnPropertyChanged(nameof(HasChartRecords));
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
        RefreshChartSeries();
        SetSelectedQueryMode(_selectedQueryMode, true);
    }

    private void RefreshChartSeries()
    {
        ChartSeries.Clear();
        ChartSeries.Add(new EdgeChartSeries
        {
            Key = ChartTotalKey,
            Title = GetText("Navigation_Capacity_TotalOutput", "产量合计"),
            Kind = EdgeChartSeriesKind.Bar,
            Axis = EdgeChartAxis.Primary,
            Brush = ResolveBrush("Edge.Brush.Chart.Accent")
        });
        ChartSeries.Add(new EdgeChartSeries
        {
            Key = ChartGoodKey,
            Title = GetText("Navigation_Capacity_Good", "良品"),
            Kind = EdgeChartSeriesKind.Bar,
            Axis = EdgeChartAxis.Primary,
            Brush = ResolveBrush("Edge.Brush.Status.Running")
        });
        ChartSeries.Add(new EdgeChartSeries
        {
            Key = ChartBadKey,
            Title = GetText("Navigation_Capacity_Bad", "不良"),
            Kind = EdgeChartSeriesKind.Bar,
            Axis = EdgeChartAxis.Primary,
            Brush = ResolveBrush("Edge.Brush.Status.Warning")
        });
        ChartSeries.Add(new EdgeChartSeries
        {
            Key = ChartYieldKey,
            Title = GetText("Navigation_Column_Yield", "良率"),
            Kind = EdgeChartSeriesKind.Line,
            Axis = EdgeChartAxis.Secondary,
            Brush = ResolveBrush("Edge.Brush.Chart.Secondary")
        });
    }

    private static double CalculateYieldPercent(DailyCapacitySnapshot row)
    {
        return row.Total <= 0 ? 0 : row.OkCount * 100d / row.Total;
    }

    private static IBrush? ResolveBrush(string resourceKey)
    {
        return global::Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var value) == true
            && value is IBrush brush
                ? brush
                : null;
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
