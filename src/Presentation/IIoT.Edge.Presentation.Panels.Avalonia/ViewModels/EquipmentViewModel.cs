using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Panels.Avalonia.ViewModels;

public sealed partial class EquipmentViewModel : AvaloniaViewModelBase
{
    private const int PageSize = 2;

    private readonly IEquipmentPanelService _equipmentPanelService;
    private readonly IDeviceService _deviceService;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly IClientPermissionService _permissionService;
    private readonly IPlcIoWriteTraceStore? _writeTraceStore;
    private readonly IAvaloniaTimer _timer;
    private int _currentPage = 1;
    private bool _isRefreshing;

    public EquipmentViewModel(
        IEquipmentPanelService equipmentPanelService,
        IDeviceService deviceService,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IAvaloniaDispatcherService dispatcherService,
        IAvaloniaRuntimeState runtimeState,
        IClientPermissionService permissionService,
        IAvaloniaTimerFactory timerFactory,
        IPlcIoWriteTraceStore? writeTraceStore = null)
    {
        _equipmentPanelService = equipmentPanelService;
        _deviceService = deviceService;
        _diagnosticsQuery = diagnosticsQuery;
        _dispatcherService = dispatcherService;
        _runtimeState = runtimeState;
        _permissionService = permissionService;
        _writeTraceStore = writeTraceStore;

        _deviceService.DeviceIdentified += OnDeviceIdentified;
        _deviceService.UploadGateChanged += OnUploadGateChanged;
        _runtimeState.StateChanged += (_, _) => _dispatcherService.Post(() => _ = RefreshAsync());
        _permissionService.PermissionStateChanged += () => _dispatcherService.Post(() => _ = RefreshAsync());

        _timer = timerFactory.Create(TimeSpan.FromSeconds(5));
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    public override string ViewId => "Core.Equipment";

    public ObservableCollection<EquipmentStatusRow> Items { get; } = [];

    public ObservableCollection<EquipmentStatusRow> PagedItems { get; } = [];

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            var normalized = Math.Clamp(value, 1, TotalPages);
            if (SetProperty(ref _currentPage, normalized))
            {
                RefreshPagedItems();
            }
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Items.Count / (double)PageSize));

    public string PageText => $"{CurrentPage} / {TotalPages}";

    public bool CanGoPrevious => CurrentPage > 1;

    public bool CanGoNext => CurrentPage < TotalPages;

    internal async Task RefreshAsync()
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        try
        {
            var hardware = await _equipmentPanelService.GetHardwareStatusAsync();
            var capacity = await _equipmentPanelService.GetCapacitySnapshotAsync();
            var diagnostics = await _diagnosticsQuery.GetCurrentAsync();
            var rows = BuildRows(hardware, capacity, diagnostics);

            await _dispatcherService.InvokeAsync(() => ReplaceRows(rows));
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private IReadOnlyList<EquipmentStatusRow> BuildRows(
        IReadOnlyList<HardwareSnapshot> hardware,
        CapacitySnapshot capacity,
        EdgeSyncDiagnosticsSnapshot diagnostics)
    {
        var device = _deviceService.CurrentDevice;
        var gate = _deviceService.CurrentUploadGate;
        var rows = new List<EquipmentStatusRow>
        {
            new(
                "运行链路",
                string.IsNullOrWhiteSpace(device?.DeviceName) ? diagnostics.DeviceName : device.DeviceName,
                FormatDeviceState(device),
                $"ClientCode={device?.ClientCode ?? "--"}；DeviceId={device?.DeviceId.ToString() ?? "--"}"),
            new(
                "Cloud",
                "云端上传闸门",
                gate.State.ToString(),
                $"原因={gate.Reason}；最近成功={FormatTime(gate.LastBootstrapSucceededAtUtc)}"),
            new(
                "Cloud",
                "云端同步",
                diagnostics.Cloud.RuntimeState.ToString(),
                $"待补传={diagnostics.Cloud.PendingRetryCount + diagnostics.Cloud.PendingPassStationCount + diagnostics.Cloud.PendingDeviceLogCount + diagnostics.Cloud.PendingCapacityCount}；死信={diagnostics.Cloud.DeadLetters?.TotalCount ?? 0}"),
            new(
                "MES",
                "MES 同步",
                diagnostics.Mes.RuntimeState.ToString(),
                $"待补传={diagnostics.Mes.PendingRetryCount}；死信={diagnostics.Mes.DeadLetters?.TotalCount ?? 0}"),
            new(
                "运行链路",
                "生产上下文",
                diagnostics.ContextPersistence.CorruptFileCount == 0 ? "正常" : "异常",
                $"今日产出={capacity.TodayOutput}；NG={capacity.NgCount}；良率={capacity.TodayYield}"),
            BuildIoWriteGateRow(hardware),
            BuildRecentPlcWriteTraceRow()
        };

        rows.AddRange(hardware
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new EquipmentStatusRow(
                "PLC",
                item.Name,
                item.IsConnected ? "已连接" : "未连接",
                $"{item.DeviceType}；{item.Address}")));

        return rows;
    }

    private void ReplaceRows(IEnumerable<EquipmentStatusRow> rows)
    {
        Items.Clear();
        foreach (var row in rows)
        {
            Items.Add(row);
        }

        RefreshPagedItems();
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanGoPrevious)
        {
            CurrentPage--;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanGoNext)
        {
            CurrentPage++;
        }
    }

    private void RefreshPagedItems()
    {
        var normalized = Math.Clamp(_currentPage, 1, TotalPages);
        if (_currentPage != normalized)
        {
            _currentPage = normalized;
            OnPropertyChanged(nameof(CurrentPage));
        }

        PagedItems.Clear();
        foreach (var row in Items.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            PagedItems.Add(row);
        }

        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private void OnDeviceIdentified(DeviceSession? session)
        => _dispatcherService.Post(() => _ = RefreshAsync());

    private void OnUploadGateChanged(EdgeUploadGateSnapshot snapshot)
        => _dispatcherService.Post(() => _ = RefreshAsync());

    private static string FormatDeviceState(DeviceSession? device)
        => device is null ? "未获取设备快照" : "已读址";

    private static string FormatTime(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "--";

    private EquipmentStatusRow BuildIoWriteGateRow(IReadOnlyCollection<HardwareSnapshot> hardware)
    {
        if (!_runtimeState.IsRuntimeStarted)
        {
            return new EquipmentStatusRow(
                "I/O",
                "写入闸门",
                "UI-only",
                "运行链路未启动，禁止申请写入运行时缓存。");
        }

        if (!_permissionService.CanEditHardware)
        {
            return new EquipmentStatusRow(
                "I/O",
                "写入闸门",
                "无权限",
                "当前用户无硬件配置权限，禁止申请 I/O 写入。");
        }

        var connectedCount = hardware.Count(static item => item.IsConnected);
        if (connectedCount == 0)
        {
            return new EquipmentStatusRow(
                "I/O",
                "写入闸门",
                "PLC 未连接",
                "没有已连接 PLC，禁止申请写入运行时缓存。");
        }

        return new EquipmentStatusRow(
            "I/O",
            "写入闸门",
            "可申请写入",
            $"已连接 PLC {connectedCount} 台，写入仍需页面确认。");
    }

    private EquipmentStatusRow BuildRecentPlcWriteTraceRow()
    {
        var trace = _writeTraceStore?.GetRecent(1).FirstOrDefault();
        if (trace is null)
        {
            return new EquipmentStatusRow(
                "PLC",
                "最近 PLC 块写入",
                "暂无",
                "本次启动尚未记录 PLC 块写入结果。");
        }

        var state = trace.Kind switch
        {
            PlcIoWriteTraceKind.Attempt => "尝试",
            PlcIoWriteTraceKind.Success => "成功",
            PlcIoWriteTraceKind.Failed => "失败",
            _ => trace.Kind.ToString()
        };
        var detail = $"设备={trace.DeviceName}；块={trace.StartAddress} / {trace.WordCount} 字；时间={trace.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        if (!string.IsNullOrWhiteSpace(trace.ErrorMessage))
        {
            detail = $"{detail}；原因={trace.ErrorMessage}";
        }

        return new EquipmentStatusRow("PLC", "最近 PLC 块写入", state, detail);
    }
}

public sealed partial class EquipmentStatusRow : ObservableObject
{
    public EquipmentStatusRow(string group, string name, string state, string lastValue)
    {
        Group = group;
        Name = name;
        State = state;
        LastValue = lastValue;
    }

    public string Group { get; }

    public string Name { get; }

    public string State { get; }

    public string StateDisplayText => NormalizeState(State);

    public bool IsStateSuccess
        => ContainsAny(StateDisplayText, "已", "正常", "成功", "可申请")
           || ContainsAny(State, "Connected", "Running", "Succeeded");

    public bool IsStateWarning
        => ContainsAny(StateDisplayText, "未", "暂无", "尝试", "无权限", "未知", "未识别", "未启动")
           || ContainsAny(State, "UI-only", "Unknown", "Unidentified", "NotStarted");

    public bool IsStateError
        => ContainsAny(StateDisplayText, "异常", "失败")
           || ContainsAny(State, "Failed", "Faulted", "Error");

    public bool IsStateMuted
        => !IsStateSuccess && !IsStateWarning && !IsStateError;

    [ObservableProperty]
    private string lastValue;

    private static string NormalizeState(string state)
        => state switch
        {
            "Unknown" => "未知",
            "DeviceUnidentified" => "未识别",
            "NotStarted" => "未启动",
            "Running" => "运行中",
            "Succeeded" => "成功",
            "Failed" => "失败",
            "Stopped" => "已停止",
            _ => state
        };

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
