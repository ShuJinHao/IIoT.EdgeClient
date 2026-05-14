using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Data;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class MonitorViewModel : NavigationPageViewModelBase
{
    private readonly IMonitorViewService _monitorViewService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaTimer _timer;
    private bool _isRefreshing;

    public MonitorViewModel(
        IMonitorViewService monitorViewService,
        IAvaloniaLanguageService languageService,
        IAvaloniaTimerFactory timerFactory,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _monitorViewService = monitorViewService;
        _languageService = languageService;
        _timer = timerFactory.Create(TimeSpan.FromSeconds(2));
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<MonitorDeviceRow> Devices { get; } = [];

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    [RelayCommand]
    private Task RefreshAsync()
        => RefreshCoreAsync();

    public override async Task OnActivatedAsync()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        await RefreshCoreAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshCoreAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        try
        {
            _isRefreshing = true;
            var snapshots = await _monitorViewService.GetSnapshotsAsync();
            ApplySnapshots(snapshots);
            FeedbackMessage = snapshots.Count == 0
                ? Text("Navigation_Monitor_NoDeviceData", "暂无数据")
                : string.Empty;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"{Text("Navigation_Monitor_LoadFailed", "加载监控数据失败。")}{ex.Message}";
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplySnapshots(IReadOnlyList<DeviceMonitorSnapshot> snapshots)
    {
        Devices.Clear();
        foreach (var snapshot in snapshots)
        {
            var row = new MonitorDeviceRow();
            row.Apply(snapshot, FormatCloudSync(snapshot), FormatMesSync(snapshot), FormatContextPersistence(snapshot));
            Devices.Add(row);
        }
    }

    private static string FormatCloudSync(DeviceMonitorSnapshot snapshot)
        => $"运行状态：{snapshot.CloudSync.RuntimeState}；待处理：过站={snapshot.CloudSync.PendingPassStationCount}，日志={snapshot.CloudSync.PendingDeviceLogCount}，产能={snapshot.CloudSync.PendingCapacityCount}";

    private static string FormatMesSync(DeviceMonitorSnapshot snapshot)
        => $"运行状态：{snapshot.MesSync.RuntimeState}；待重试={snapshot.MesSync.PendingRetryCount}";

    private static string FormatContextPersistence(DeviceMonitorSnapshot snapshot)
        => $"损坏文件数：{snapshot.ContextPersistence.CorruptFileCount}";

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed partial class MonitorDeviceRow : ObservableObject
{
    [ObservableProperty]
    private string deviceName = string.Empty;

    [ObservableProperty]
    private int dayShiftOk;

    [ObservableProperty]
    private int dayShiftNg;

    [ObservableProperty]
    private int dayShiftTotal;

    [ObservableProperty]
    private string dayShiftYield = "0%";

    [ObservableProperty]
    private int nightShiftOk;

    [ObservableProperty]
    private int nightShiftNg;

    [ObservableProperty]
    private int nightShiftTotal;

    [ObservableProperty]
    private string nightShiftYield = "0%";

    [ObservableProperty]
    private int totalAll;

    [ObservableProperty]
    private int okAll;

    [ObservableProperty]
    private int ngAll;

    [ObservableProperty]
    private string yieldAll = "0%";

    [ObservableProperty]
    private string deviceDataSummary = string.Empty;

    [ObservableProperty]
    private string stepSummary = string.Empty;

    [ObservableProperty]
    private int cellCount;

    [ObservableProperty]
    private DataTable? cellTable;

    [ObservableProperty]
    private string cloudSyncStatus = string.Empty;

    [ObservableProperty]
    private string mesSyncStatus = string.Empty;

    [ObservableProperty]
    private string contextPersistenceStatus = string.Empty;

    public void Apply(
        DeviceMonitorSnapshot snapshot,
        string cloudSyncStatus,
        string mesSyncStatus,
        string contextPersistenceStatus)
    {
        DeviceName = snapshot.DeviceName;
        DayShiftOk = snapshot.DayShiftOk;
        DayShiftNg = snapshot.DayShiftNg;
        DayShiftTotal = snapshot.DayShiftTotal;
        DayShiftYield = snapshot.DayShiftYield;
        NightShiftOk = snapshot.NightShiftOk;
        NightShiftNg = snapshot.NightShiftNg;
        NightShiftTotal = snapshot.NightShiftTotal;
        NightShiftYield = snapshot.NightShiftYield;
        TotalAll = snapshot.TotalAll;
        OkAll = snapshot.OkAll;
        NgAll = snapshot.NgAll;
        YieldAll = snapshot.YieldAll;
        DeviceDataSummary = snapshot.DeviceDataSummary;
        StepSummary = snapshot.StepSummary;
        CellCount = snapshot.CellCount;
        CellTable = snapshot.CellTable;
        CloudSyncStatus = cloudSyncStatus;
        MesSyncStatus = mesSyncStatus;
        ContextPersistenceStatus = contextPersistenceStatus;
    }
}
