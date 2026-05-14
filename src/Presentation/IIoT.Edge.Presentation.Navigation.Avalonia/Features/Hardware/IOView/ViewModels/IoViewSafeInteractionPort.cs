using System.Globalization;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public interface IIoViewSafeInteractionPort
{
    Task<IoViewReadResult> ReadAsync(IoNetworkDeviceModel device, CancellationToken cancellationToken);

    Task<IoViewWriteResult> WriteAsync(
        IoNetworkDeviceModel device,
        IoInteractionRowModel row,
        int value,
        CancellationToken cancellationToken);
}

public sealed record IoViewReadResult(bool ShouldRefreshPreview, string? ErrorMessage = null);

public enum IoViewWriteResultKind
{
    AcceptedToRuntimeBuffer = 0,
    RuntimeNotStarted = 1,
    NoPermission = 2,
    DeviceNotBound = 3,
    PlcDisconnected = 4,
    NoWritableSignal = 5,
    InvalidValue = 6,
    RejectedByUser = 7,
    BufferUnavailable = 8
}

public sealed record IoViewWriteResult(IoViewWriteResultKind Kind, string Message)
{
    public bool Accepted => Kind == IoViewWriteResultKind.AcceptedToRuntimeBuffer;
}

public sealed record IoViewWriteGateAuditEntry(
    DateTimeOffset OccurredAt,
    string DeviceName,
    string BusinessGroup,
    IoViewWriteResultKind Kind,
    string Message,
    int? Value);

public interface IIoViewWriteGateAuditStore
{
    void Record(IoViewWriteGateAuditEntry entry);

    IReadOnlyList<IoViewWriteGateAuditEntry> GetRecent(int count = 20);
}

public sealed class IoViewWriteGateAuditStore : IIoViewWriteGateAuditStore
{
    private readonly object _syncRoot = new();
    private readonly LinkedList<IoViewWriteGateAuditEntry> _entries = [];

    public void Record(IoViewWriteGateAuditEntry entry)
    {
        lock (_syncRoot)
        {
            _entries.AddFirst(entry);
            while (_entries.Count > 50)
            {
                _entries.RemoveLast();
            }
        }
    }

    public IReadOnlyList<IoViewWriteGateAuditEntry> GetRecent(int count = 20)
    {
        lock (_syncRoot)
        {
            return _entries.Take(Math.Max(1, count)).ToArray();
        }
    }
}

/// <summary>
/// I/O 页面的受控写入端口。它只写运行时缓冲，实际 PLC 写入仍由运行链路按块策略处理。
/// </summary>
public sealed class RuntimeBufferIoViewSafeInteractionPort : IIoViewSafeInteractionPort
{
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly IClientPermissionService _permissionService;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IPlcDataStore _plcDataStore;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly ILogService _logger;
    private readonly IIoViewWriteGateAuditStore _auditStore;

    public RuntimeBufferIoViewSafeInteractionPort(
        IAvaloniaRuntimeState runtimeState,
        IClientPermissionService permissionService,
        IPlcConnectionManager plcConnectionManager,
        IPlcDataStore plcDataStore,
        IAvaloniaDialogService dialogService,
        IAvaloniaLanguageService languageService,
        ILogService logger,
        IIoViewWriteGateAuditStore auditStore)
    {
        _runtimeState = runtimeState;
        _permissionService = permissionService;
        _plcConnectionManager = plcConnectionManager;
        _plcDataStore = plcDataStore;
        _dialogService = dialogService;
        _languageService = languageService;
        _logger = logger;
        _auditStore = auditStore;
    }

    public Task<IoViewReadResult> ReadAsync(IoNetworkDeviceModel device, CancellationToken cancellationToken)
        => Task.FromResult(new IoViewReadResult(ShouldRefreshPreview: true));

    public async Task<IoViewWriteResult> WriteAsync(
        IoNetworkDeviceModel device,
        IoInteractionRowModel row,
        int value,
        CancellationToken cancellationToken)
    {
        if (!_runtimeState.IsRuntimeStarted)
        {
            return Reject(IoViewWriteResultKind.RuntimeNotStarted, "Navigation_Io_Write_RuntimeNotStarted", "运行链路未启动，不能申请写入运行时缓冲。", device, row, value);
        }

        if (!_permissionService.CanEditHardware)
        {
            return Reject(IoViewWriteResultKind.NoPermission, "Navigation_Io_Write_NoPermission", "当前用户没有硬件配置权限，不能申请 I/O 写入。", device, row, value);
        }

        if (device.Id <= 0)
        {
            return Reject(IoViewWriteResultKind.DeviceNotBound, "Navigation_Io_Write_DeviceNotBound", "当前设备未绑定运行时设备，不能申请 I/O 写入。", device, row, value);
        }

        if (row.HostSignals.Count == 0)
        {
            return Reject(IoViewWriteResultKind.NoWritableSignal, "Navigation_Io_Write_NoWritableSignal", "当前交互行没有上位机到 PLC 的可写信号。", device, row, value);
        }

        if (value is < 0 or > ushort.MaxValue)
        {
            return Reject(IoViewWriteResultKind.InvalidValue, "Navigation_Io_Write_InvalidValue", "写入值必须在 0 到 65535 之间。", device, row, value);
        }

        var runtimeStatus = _plcConnectionManager.GetRuntimeStatus(device.Id);
        if (runtimeStatus?.IsConnected != true)
        {
            return Reject(IoViewWriteResultKind.PlcDisconnected, "Navigation_Io_Write_PlcDisconnected", "PLC 当前未连接，不能申请写入运行时缓冲。", device, row, value);
        }

        var buffer = _plcDataStore.GetBuffer(device.Id);
        if (buffer is null)
        {
            return Reject(IoViewWriteResultKind.BufferUnavailable, "Navigation_Io_Write_BufferUnavailable", "当前设备没有运行时缓冲，不能申请 I/O 写入。", device, row, value);
        }

        var confirmed = await _dialogService.ConfirmAsync(
            Text("Navigation_Io_Write_ConfirmTitle", "确认 I/O 写入"),
            string.Format(
                CultureInfo.CurrentCulture,
                Text("Navigation_Io_Write_ConfirmMessageFormat", "将把 {0} 的交互项“{1}”写入运行时缓冲，值为 {2}。实际 PLC 写入仍等待运行链路按块策略处理。"),
                device.DeviceName,
                row.BusinessGroup,
                value));

        if (!confirmed)
        {
            return Reject(IoViewWriteResultKind.RejectedByUser, "Navigation_Io_Write_RejectedByUser", "用户已取消，本次未写入运行时缓冲。", device, row, value);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var writeValue = (ushort)value;
        foreach (var signal in row.HostSignals)
        {
            buffer.SetWriteValue(signal.SignalKey, 0, writeValue);
            buffer.SetWriteValue(signal.StartIndex, writeValue);
            signal.SetValue(writeValue);
        }

        row.NotifyValuesChanged();
        var message = string.Format(
            CultureInfo.CurrentCulture,
            Text("Navigation_Io_Write_AcceptedToRuntimeBuffer", "已写入运行时缓冲：{0} / {1} = {2}。实际 PLC 写入由运行链路按块策略处理。"),
            device.DeviceName,
            row.BusinessGroup,
            value);
        _logger.Info(message);
        var result = new IoViewWriteResult(IoViewWriteResultKind.AcceptedToRuntimeBuffer, message);
        Record(device, row, result, value);
        return result;
    }

    private IoViewWriteResult Reject(
        IoViewWriteResultKind kind,
        string resourceKey,
        string fallback,
        IoNetworkDeviceModel? device = null,
        IoInteractionRowModel? row = null,
        int? value = null)
    {
        var result = new IoViewWriteResult(kind, Text(resourceKey, fallback));
        Record(device, row, result, value);
        return result;
    }

    private void Record(
        IoNetworkDeviceModel? device,
        IoInteractionRowModel? row,
        IoViewWriteResult result,
        int? value)
    {
        _auditStore.Record(new IoViewWriteGateAuditEntry(
            DateTimeOffset.Now,
            device?.DeviceName ?? "--",
            row?.BusinessGroup ?? "--",
            result.Kind,
            result.Message,
            value));
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

/// <summary>
/// 测试和隔离场景使用的空实现，生产注册必须使用 RuntimeBufferIoViewSafeInteractionPort。
/// </summary>
public sealed class NoopIoViewSafeInteractionPort : IIoViewSafeInteractionPort
{
    public Task<IoViewReadResult> ReadAsync(IoNetworkDeviceModel device, CancellationToken cancellationToken)
        => Task.FromResult(new IoViewReadResult(ShouldRefreshPreview: true));

    public Task<IoViewWriteResult> WriteAsync(
        IoNetworkDeviceModel device,
        IoInteractionRowModel row,
        int value,
        CancellationToken cancellationToken)
        => Task.FromResult(new IoViewWriteResult(
            IoViewWriteResultKind.RuntimeNotStarted,
            "运行链路未启动，不能申请写入运行时缓冲。"));
}
