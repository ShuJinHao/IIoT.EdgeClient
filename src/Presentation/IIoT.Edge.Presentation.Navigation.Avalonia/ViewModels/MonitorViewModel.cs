using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class MonitorViewModel : NavigationPageViewModelBase
{
    private readonly IMonitorViewService _monitorViewService;
    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly ILogService _logService;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly MonitorStatusFormatter _statusFormatter;
    private readonly IAvaloniaTimer _timer;
    private bool _isRefreshing;

    public MonitorViewModel(
        IMonitorViewService monitorViewService,
        IEquipmentPanelService equipmentPanelService,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        ILogService logService,
        IAvaloniaRuntimeState runtimeState,
        IAvaloniaLanguageService languageService,
        IAvaloniaTimerFactory timerFactory,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _monitorViewService = monitorViewService;
        _equipmentPanelService = equipmentPanelService;
        _diagnosticsQuery = diagnosticsQuery;
        _logService = logService;
        _runtimeState = runtimeState;
        _statusFormatter = new MonitorStatusFormatter(languageService);
        _timer = timerFactory.Create(TimeSpan.FromSeconds(2));
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<MonitorDeviceRow> Devices { get; } = [];

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    [ObservableProperty]
    private string totalOutputText = "0";

    [ObservableProperty]
    private string okYieldText = "0%";

    [ObservableProperty]
    private string ngTotalText = "0";

    [ObservableProperty]
    private string deviceCountText = "0 / 0";

    [ObservableProperty]
    private string plcStatusText = string.Empty;

    [ObservableProperty]
    private string plcStatusDetailText = string.Empty;

    [ObservableProperty]
    private bool plcStatusIsSuccess;

    [ObservableProperty]
    private bool plcStatusIsWarning = true;

    [ObservableProperty]
    private bool plcStatusIsError;

    [ObservableProperty]
    private string cloudStatusText = string.Empty;

    [ObservableProperty]
    private string cloudStatusDetailText = string.Empty;

    [ObservableProperty]
    private bool cloudStatusIsSuccess;

    [ObservableProperty]
    private bool cloudStatusIsWarning = true;

    [ObservableProperty]
    private bool cloudStatusIsError;

    [ObservableProperty]
    private string mesStatusText = string.Empty;

    [ObservableProperty]
    private string mesStatusDetailText = string.Empty;

    [ObservableProperty]
    private bool mesStatusIsSuccess;

    [ObservableProperty]
    private bool mesStatusIsWarning = true;

    [ObservableProperty]
    private bool mesStatusIsError;

    [ObservableProperty]
    private string cacheQueueStatusText = string.Empty;

    [ObservableProperty]
    private string cacheQueueDetailText = string.Empty;

    [ObservableProperty]
    private bool cacheQueueIsSuccess;

    [ObservableProperty]
    private bool cacheQueueIsWarning = true;

    [ObservableProperty]
    private bool cacheQueueIsError;

    [ObservableProperty]
    private string latestAlertText = string.Empty;

    [ObservableProperty]
    private string latestAlertDetailText = string.Empty;

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
            var snapshotsTask = _monitorViewService.GetSnapshotsAsync();
            var hardwareTask = _equipmentPanelService.GetHardwareStatusAsync();
            var diagnosticsTask = _diagnosticsQuery.GetCurrentAsync();

            await Task.WhenAll(snapshotsTask, hardwareTask, diagnosticsTask);

            var snapshots = await snapshotsTask;
            ApplySnapshots(snapshots);
            ApplyHardwareStatus(await hardwareTask);
            ApplyDiagnostics(await diagnosticsTask);
            ApplyLatestAlert();
            FeedbackMessage = snapshots.Count == 0
                ? _statusFormatter.Text("Navigation_Monitor_NoDeviceData", "暂无设备产量快照")
                : string.Empty;
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"{_statusFormatter.Text("Navigation_Monitor_LoadFailed", "加载监控数据失败：")}{ex.Message}";
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
            row.Apply(
                snapshot,
                _statusFormatter.FormatCloudRow(snapshot),
                _statusFormatter.FormatMesRow(snapshot),
                _statusFormatter.FormatContextRow(snapshot));
            Devices.Add(row);
        }

        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var total = Devices.Sum(static device => device.TotalAll);
        var ok = Devices.Sum(static device => device.OkAll);
        var ng = Devices.Sum(static device => device.NgAll);
        var yield = total == 0 ? 0 : ok * 100d / total;

        TotalOutputText = total.ToString("N0", CultureInfo.CurrentCulture);
        OkYieldText = string.Format(CultureInfo.CurrentCulture, "{0:0.00}%", yield);
        NgTotalText = ng.ToString("N0", CultureInfo.CurrentCulture);

        var maxOutput = Math.Max(1, Devices.Count == 0 ? 1 : Devices.Max(static device => device.TotalAll));
        foreach (var device in Devices)
        {
            device.OutputPercent = device.TotalAll * 100d / maxOutput;
        }
    }

    private void ApplyHardwareStatus(IReadOnlyList<HardwareSnapshot> hardware)
    {
        var card = _statusFormatter.BuildPlc(hardware, _runtimeState.IsRuntimeStarted);
        DeviceCountText = card.CountText;
        PlcStatusText = card.StatusText;
        PlcStatusDetailText = card.DetailText;
        ApplyPlcVisual(card.Visual);
    }

    private void ApplyDiagnostics(EdgeSyncDiagnosticsSnapshot diagnostics)
    {
        var cloud = _statusFormatter.BuildCloud(diagnostics.Cloud);
        CloudStatusText = cloud.StatusText;
        CloudStatusDetailText = cloud.DetailText;
        ApplyCloudVisual(cloud.Visual);

        var mes = _statusFormatter.BuildMes(diagnostics.Mes);
        MesStatusText = mes.StatusText;
        MesStatusDetailText = mes.DetailText;
        ApplyMesVisual(mes.Visual);

        var cache = _statusFormatter.BuildCache(diagnostics);
        CacheQueueStatusText = cache.StatusText;
        CacheQueueDetailText = cache.DetailText;
        ApplyCacheVisual(cache.Visual);
    }

    private void ApplyLatestAlert()
    {
        var alert = _statusFormatter.BuildLatestAlert(_logService);
        LatestAlertText = alert.Text;
        LatestAlertDetailText = alert.DetailText;
    }

    private void ApplyPlcVisual(MonitorStatusVisual visual)
    {
        PlcStatusIsSuccess = visual == MonitorStatusVisual.Success;
        PlcStatusIsWarning = visual == MonitorStatusVisual.Warning;
        PlcStatusIsError = visual == MonitorStatusVisual.Error;
    }

    private void ApplyCloudVisual(MonitorStatusVisual visual)
    {
        CloudStatusIsSuccess = visual == MonitorStatusVisual.Success;
        CloudStatusIsWarning = visual == MonitorStatusVisual.Warning;
        CloudStatusIsError = visual == MonitorStatusVisual.Error;
    }

    private void ApplyMesVisual(MonitorStatusVisual visual)
    {
        MesStatusIsSuccess = visual == MonitorStatusVisual.Success;
        MesStatusIsWarning = visual == MonitorStatusVisual.Warning;
        MesStatusIsError = visual == MonitorStatusVisual.Error;
    }

    private void ApplyCacheVisual(MonitorStatusVisual visual)
    {
        CacheQueueIsSuccess = visual == MonitorStatusVisual.Success;
        CacheQueueIsWarning = visual == MonitorStatusVisual.Warning;
        CacheQueueIsError = visual == MonitorStatusVisual.Error;
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
    private double outputPercent;

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
