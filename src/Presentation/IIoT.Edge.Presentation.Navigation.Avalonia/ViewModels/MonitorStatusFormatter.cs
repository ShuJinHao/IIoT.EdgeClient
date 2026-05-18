using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Globalization;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

internal sealed class MonitorStatusFormatter(IAvaloniaLanguageService languageService)
{
    public MonitorStatusCard BuildPlc(IReadOnlyList<HardwareSnapshot> hardware, bool runtimeStarted)
    {
        var total = hardware.Count;
        var online = hardware.Count(static device => device.IsConnected);
        var countText = string.Format(CultureInfo.CurrentCulture, "{0} / {1}", online, total);

        if (!runtimeStarted)
        {
            return new MonitorStatusCard(
                Text("Navigation_Monitor_StatusNoRuntime", "未启动"),
                Text("Navigation_Monitor_RuntimeNotStarted", "运行链路未启动，未读取 PLC 在线状态。"),
                countText,
                MonitorStatusVisual.Warning);
        }

        var detail = total == 0
            ? Text("Navigation_Monitor_PlcNoDevices", "未配置 PLC 设备")
            : Format("Navigation_Monitor_PlcOnlineFormat", "在线 {0} / {1}", online, total);

        if (total == 0)
        {
            return new MonitorStatusCard(Text("Navigation_Monitor_StatusNoData", "无数据"), detail, countText, MonitorStatusVisual.Warning);
        }

        if (online == total)
        {
            return new MonitorStatusCard(Text("Navigation_Monitor_StatusOnline", "全部在线"), detail, countText, MonitorStatusVisual.Success);
        }

        return online == 0
            ? new MonitorStatusCard(Text("Navigation_Monitor_StatusOffline", "全部离线"), detail, countText, MonitorStatusVisual.Error)
            : new MonitorStatusCard(Text("Navigation_Monitor_StatusPartial", "部分在线"), detail, countText, MonitorStatusVisual.Warning);
    }

    public MonitorStatusCard BuildCloud(CloudSyncDiagnosticsSnapshot cloud)
        => new(
            FormatCloudStatus(cloud),
            Format(
                "Navigation_Monitor_CloudDetailFormat",
                "过站 {0} / 日志 {1} / 产能 {2} / retry {3}",
                cloud.PendingPassStationCount,
                cloud.PendingDeviceLogCount,
                cloud.PendingCapacityCount,
                cloud.PendingRetryCount),
            string.Empty,
            GetCloudVisual(cloud));

    public MonitorStatusCard BuildMes(MesSyncDiagnosticsSnapshot mes)
        => new(
            FormatMesStatus(mes),
            Format(
                "Navigation_Monitor_MesDetailFormat",
                "待重试 {0} / 通道 {1}",
                mes.PendingRetryCount,
                mes.Channels.Count),
            string.Empty,
            GetMesVisual(mes));

    public MonitorStatusCard BuildCache(EdgeSyncDiagnosticsSnapshot diagnostics)
    {
        var cloudDeadLetters = diagnostics.Cloud.DeadLetters?.TotalCount ?? 0;
        var mesDeadLetters = diagnostics.Mes.DeadLetters?.TotalCount ?? 0;
        var deadLetters = cloudDeadLetters + mesDeadLetters;
        var pendingCloud = diagnostics.Cloud.PendingPassStationCount
            + diagnostics.Cloud.PendingDeviceLogCount
            + diagnostics.Cloud.PendingCapacityCount
            + diagnostics.Cloud.PendingRetryCount;
        var pendingMes = diagnostics.Mes.PendingRetryCount;
        var corruptFiles = diagnostics.ContextPersistence.CorruptFileCount;
        var persistenceFaulted = diagnostics.Cloud.IsPersistenceFaulted
            || diagnostics.Mes.IsPersistenceFaulted
            || diagnostics.Cloud.DeadLetters?.IsPersistenceFaulted == true
            || diagnostics.Mes.DeadLetters?.IsPersistenceFaulted == true;

        var detail = Format(
            "Navigation_Monitor_CacheDetailFormat",
            "Cloud 待处理 {0} / MES 待重试 {1} / 死信 {2} / 损坏文件 {3}",
            pendingCloud,
            pendingMes,
            deadLetters,
            corruptFiles);

        if (persistenceFaulted || corruptFiles > 0 || deadLetters > 0)
        {
            return new MonitorStatusCard(Text("Navigation_Monitor_StatusNeedsAction", "需处理"), detail, string.Empty, MonitorStatusVisual.Error);
        }

        return pendingCloud + pendingMes > 0
            ? new MonitorStatusCard(Text("Navigation_Monitor_StatusPendingSync", "待同步"), detail, string.Empty, MonitorStatusVisual.Warning)
            : new MonitorStatusCard(Text("Navigation_Monitor_StatusNoBacklog", "无积压"), detail, string.Empty, MonitorStatusVisual.Success);
    }

    public MonitorLatestAlert BuildLatestAlert(ILogService logService)
    {
        var latestError = logService is ILogDisplayService displayService
            ? displayService.Entries
                .Reverse()
                .FirstOrDefault(static entry => IsErrorLevel(entry.Level))
            : null;

        return latestError is null
            ? new MonitorLatestAlert(
                Text("Navigation_Monitor_LatestAlertNone", "暂无 ERROR/FATAL 日志"),
                Text("Navigation_Monitor_LatestAlertEmptyDetail", "来自运行日志过滤结果。"))
            : new MonitorLatestAlert(
                latestError.Message,
                Format(
                    "Navigation_Monitor_LatestAlertDetailFormat",
                    "{0} / {1:yyyy-MM-dd HH:mm:ss}",
                    latestError.Level,
                    latestError.Time));
    }

    public string FormatCloudRow(DeviceMonitorSnapshot snapshot)
        => Format(
            "Navigation_Monitor_RowCloudSyncFormat",
            "运行状态：{0}；待处理：过站 {1}，日志 {2}，产能 {3}",
            snapshot.CloudSync.RuntimeState,
            snapshot.CloudSync.PendingPassStationCount,
            snapshot.CloudSync.PendingDeviceLogCount,
            snapshot.CloudSync.PendingCapacityCount);

    public string FormatMesRow(DeviceMonitorSnapshot snapshot)
        => Format(
            "Navigation_Monitor_RowMesSyncFormat",
            "运行状态：{0}；待重试 {1}",
            snapshot.MesSync.RuntimeState,
            snapshot.MesSync.PendingRetryCount);

    public string FormatContextRow(DeviceMonitorSnapshot snapshot)
        => Format(
            "Navigation_Monitor_RowContextPersistenceFormat",
            "损坏文件数：{0}",
            snapshot.ContextPersistence.CorruptFileCount);

    private string FormatCloudStatus(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (cloud.IsPersistenceFaulted)
        {
            return Text("Navigation_Monitor_StatusPersistenceFault", "持久化异常");
        }

        if (cloud.GateState == EdgeUploadGateState.Blocked || cloud.IsCapacityBlocked)
        {
            return Text("Navigation_Monitor_StatusBlocked", "阻塞");
        }

        if (cloud.LastOutcome != CloudCallOutcome.Success && cloud.LastFailureAt.HasValue)
        {
            return Text("Navigation_Monitor_StatusFaulted", "异常");
        }

        return cloud.RuntimeState switch
        {
            CloudRetryRuntimeState.Idle => Text("Navigation_Monitor_StatusIdle", "空闲"),
            CloudRetryRuntimeState.Retrying => Text("Navigation_Monitor_StatusRetrying", "重试中"),
            CloudRetryRuntimeState.Backoff => Text("Navigation_Monitor_StatusBackoff", "退避"),
            CloudRetryRuntimeState.WaitingForRecovery => Text("Navigation_Monitor_StatusWaitingRecovery", "等待恢复"),
            _ => cloud.RuntimeState.ToString()
        };
    }

    private string FormatMesStatus(MesSyncDiagnosticsSnapshot mes)
    {
        if (mes.IsPersistenceFaulted)
        {
            return Text("Navigation_Monitor_StatusPersistenceFault", "持久化异常");
        }

        if (mes.IsCapacityBlocked)
        {
            return Text("Navigation_Monitor_StatusBlocked", "阻塞");
        }

        return mes.RuntimeState switch
        {
            MesRetryRuntimeState.Idle => Text("Navigation_Monitor_StatusIdle", "空闲"),
            MesRetryRuntimeState.Retrying => Text("Navigation_Monitor_StatusRetrying", "重试中"),
            MesRetryRuntimeState.Backoff => Text("Navigation_Monitor_StatusBackoff", "退避"),
            MesRetryRuntimeState.LastFailed => Text("Navigation_Monitor_StatusFaulted", "异常"),
            _ => mes.RuntimeState.ToString()
        };
    }

    private static MonitorStatusVisual GetCloudVisual(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (cloud.IsPersistenceFaulted
            || cloud.DeadLetters?.TotalCount > 0
            || cloud.DeadLetters?.IsPersistenceFaulted == true
            || cloud.GateState == EdgeUploadGateState.Blocked
            || cloud.IsCapacityBlocked
            || (cloud.LastOutcome != CloudCallOutcome.Success && cloud.LastFailureAt.HasValue))
        {
            return MonitorStatusVisual.Error;
        }

        var pending = cloud.PendingPassStationCount
            + cloud.PendingDeviceLogCount
            + cloud.PendingCapacityCount
            + cloud.PendingRetryCount;
        return cloud.RuntimeState == CloudRetryRuntimeState.Idle
               && cloud.GateState == EdgeUploadGateState.Ready
               && pending == 0
            ? MonitorStatusVisual.Success
            : MonitorStatusVisual.Warning;
    }

    private static MonitorStatusVisual GetMesVisual(MesSyncDiagnosticsSnapshot mes)
    {
        if (mes.IsPersistenceFaulted
            || mes.DeadLetters?.TotalCount > 0
            || mes.DeadLetters?.IsPersistenceFaulted == true
            || mes.IsCapacityBlocked
            || mes.RuntimeState == MesRetryRuntimeState.LastFailed)
        {
            return MonitorStatusVisual.Error;
        }

        return mes.RuntimeState == MesRetryRuntimeState.Idle && mes.PendingRetryCount == 0
            ? MonitorStatusVisual.Success
            : MonitorStatusVisual.Warning;
    }

    private string Format(string key, string fallback, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Text(key, fallback), args);

    public string Text(string key, string fallback)
    {
        var value = languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private static bool IsErrorLevel(string? level)
        => !string.IsNullOrWhiteSpace(level)
           && (level.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
               || level.Contains("FATAL", StringComparison.OrdinalIgnoreCase));
}

internal sealed record MonitorStatusCard(
    string StatusText,
    string DetailText,
    string CountText,
    MonitorStatusVisual Visual);

internal sealed record MonitorLatestAlert(string Text, string DetailText);

internal enum MonitorStatusVisual
{
    Success,
    Warning,
    Error
}
