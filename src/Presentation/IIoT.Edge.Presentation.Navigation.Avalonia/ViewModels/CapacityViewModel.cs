using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class CapacityViewModel : NavigationPageViewModelBase
{
    private readonly ICapacityViewService _capacityViewService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private bool _isActivated;
    private bool _isRefreshingDeviceList;

    public CapacityViewModel(
        ICapacityViewService capacityViewService,
        IAvaloniaLanguageService languageService,
        IAvaloniaDialogService dialogService,
        IAvaloniaDispatcherService dispatcherService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _capacityViewService = capacityViewService;
        _languageService = languageService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
    }

    public ObservableCollection<string> DeviceNames { get; } = [];

    public ObservableCollection<DailyCapacityVm> Records { get; } = [];

    public ObservableCollection<CapacityChartBarVm> ChartBars { get; } = [];

    public IReadOnlyList<string> QueryModes { get; } =
    [
        CapacityQueryModes.Day,
        CapacityQueryModes.Month,
        CapacityQueryModes.Year
    ];

    [ObservableProperty]
    private string selectedDeviceName = string.Empty;

    [ObservableProperty]
    private string selectedQueryMode = CapacityQueryModes.Day;

    [ObservableProperty]
    private DateTime queryDate = DateTime.Today;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private int periodTotal;

    [ObservableProperty]
    private int periodOk;

    [ObservableProperty]
    private int periodNg;

    [ObservableProperty]
    private string periodYield = "0%";

    [ObservableProperty]
    private string avgDaily = "0";

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public bool CanQueryCloud => IsOnline;

    public override async Task OnActivatedAsync()
    {
        if (!_isActivated)
        {
            _capacityViewService.UploadGateChanged += OnUploadGateChanged;
            _isActivated = true;
        }

        RefreshDeviceList();
        IsOnline = _capacityViewService.IsOnline;
        await LoadCurrentDataAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        if (_isActivated)
        {
            _capacityViewService.UploadGateChanged -= OnUploadGateChanged;
            _isActivated = false;
        }

        return Task.CompletedTask;
    }

    public void OnCapacityUpdated()
        => _dispatcherService.Post(() => _ = LoadCurrentDataAsync());

    partial void OnSelectedDeviceNameChanged(string value)
    {
        if (_isActivated && !_isRefreshingDeviceList)
        {
            _ = LoadCurrentDataAsync();
        }
    }

    partial void OnIsOnlineChanged(bool value)
        => OnPropertyChanged(nameof(CanQueryCloud));

    [RelayCommand]
    private async Task QueryAsync()
    {
        if (!CanQueryCloud)
        {
            await _dialogService.ShowInfoAsync(
                ViewTitle,
                Text("Navigation_Capacity_OfflineHint", "设备上传授权尚未就绪，暂时无法查询云端产能。"));
            ClearData();
            return;
        }

        try
        {
            var result = await _capacityViewService.QueryHistoryAsync(
                SelectedQueryMode,
                QueryDate,
                SelectedDeviceName);
            ApplyResult(result);
            FeedbackMessage = string.Empty;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"{Text("Navigation_Capacity_QueryFailed", "产能查询失败。")}{ex.Message}";
        }
    }

    [RelayCommand]
    private void Export()
    {
        FeedbackMessage = "当前 Avalonia 迁移批次不写出导出文件。";
    }

    private void OnUploadGateChanged(EdgeUploadGateSnapshot snapshot)
        => _dispatcherService.Post(() =>
        {
            IsOnline = snapshot.State == EdgeUploadGateState.Ready;
            _ = LoadCurrentDataAsync();
        });

    private void RefreshDeviceList()
    {
        var names = _capacityViewService.GetDeviceNames();
        DeviceNames.Clear();
        foreach (var name in names)
        {
            DeviceNames.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(SelectedDeviceName) && names.Contains(SelectedDeviceName))
        {
            return;
        }

        var nextDeviceName = names.FirstOrDefault() ?? string.Empty;
        if (!string.Equals(SelectedDeviceName, nextDeviceName, StringComparison.Ordinal))
        {
            _isRefreshingDeviceList = true;
            try
            {
                SelectedDeviceName = nextDeviceName;
            }
            finally
            {
                _isRefreshingDeviceList = false;
            }
        }
    }

    private async Task LoadCurrentDataAsync()
    {
        if (!CanQueryCloud)
        {
            ClearData();
            return;
        }

        try
        {
            var result = await _capacityViewService.LoadTodayAsync(SelectedDeviceName);
            ApplyResult(result);
            FeedbackMessage = string.Empty;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"{Text("Navigation_Capacity_LoadFailed", "加载产能数据失败。")}{ex.Message}";
        }
    }

    private void ApplyResult(CapacityViewResult result)
    {
        Records.Clear();
        foreach (var row in result.Rows)
        {
            Records.Add(row);
        }

        PeriodTotal = result.PeriodTotal;
        PeriodOk = result.PeriodOk;
        PeriodNg = result.PeriodNg;
        PeriodYield = result.PeriodYield;
        AvgDaily = result.AvgDaily;
        RefreshChart();
    }

    private void ClearData()
    {
        Records.Clear();
        PeriodTotal = 0;
        PeriodOk = 0;
        PeriodNg = 0;
        PeriodYield = "0%";
        AvgDaily = "0";
        RefreshChart();
    }

    private void RefreshChart()
    {
        ChartBars.Clear();
        var max = Records.Count > 0 ? Records.Max(static item => item.Total) : 0;
        var safeMax = max <= 0 ? 1 : max;

        foreach (var row in Records)
        {
            var ratio = row.Total * 1.0 / safeMax;
            ChartBars.Add(new CapacityChartBarVm
            {
                Label = string.IsNullOrWhiteSpace(row.DateFull) ? row.Date : row.DateFull,
                Value = row.Total,
                HeightRatio = ratio,
                ChartHeight = Math.Max(2, ratio * 160)
            });
        }
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
