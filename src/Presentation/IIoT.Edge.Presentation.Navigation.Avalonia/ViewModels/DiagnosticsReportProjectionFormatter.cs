using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

internal static class DiagnosticsReportProjectionFormatter
{
    internal static string BuildConfigurationProfileText(ConfigurationProfileSnapshot profile)
    {
        var machine = string.IsNullOrWhiteSpace(profile.MachineProfile)
            ? "未配置"
            : profile.MachineProfile;
        var loaded = profile.IsMachineProfileLoaded ? "已加载" : "未加载";
        return $"环境：{profile.EnvironmentName}；机型：{machine}（{loaded}）；运行目录：{profile.RuntimeDataRoot}";
    }

    internal static string BuildDetailText(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot? diagnostics)
    {
        var profile = report.ConfigurationProfile;
        var cloud = diagnostics is null
            ? "云端同步：未接入"
            : $"云端同步：{diagnostics.Cloud.RuntimeState}，闸门 {diagnostics.Cloud.GateState}，待补传 {diagnostics.Cloud.PendingRetryCount + diagnostics.Cloud.PendingPassStationCount + diagnostics.Cloud.PendingDeviceLogCount + diagnostics.Cloud.PendingCapacityCount}";
        var mes = diagnostics is null
            ? "MES 同步：未接入"
            : $"MES 同步：{diagnostics.Mes.RuntimeState}，待补传 {diagnostics.Mes.PendingRetryCount}";

        return $"运行目录：{profile.RuntimeDataRoot}；模块 {report.ModuleRegistrations.Count} 个；插件 {report.PluginStates.Count} 个；{cloud}；{mes}";
    }

    internal static string FormatRegistered(bool value) => value ? "已注册" : "未注册";

    internal static string FormatEnabled(bool value) => value ? "已启用" : "未启用";

    internal static string FormatTime(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "--";

    internal static string FormatBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value;

    internal static string FormatTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    internal static string FormatRuntimeModeStatus(AvaloniaRuntimeStateSnapshot snapshot)
        => snapshot.Status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "UI-only",
            AvaloniaRuntimeStatus.Running => "--start-runtime",
            _ => snapshot.StatusText
        };

    internal static string BuildRuntimeModeMessage(AvaloniaRuntimeStateSnapshot snapshot)
    {
        var modeMessage = snapshot.Status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "当前为 UI-only，运行链路未启动；需要现场联调时应从运行联调入口或传入 --start-runtime。",
            AvaloniaRuntimeStatus.Starting => "已接收到 --start-runtime，运行链路正在启动；诊断页只读展示当前启动过程。",
            AvaloniaRuntimeStatus.Running => "已通过 --start-runtime 启动运行链路；诊断页展示状态，死信运维区只处理本地 Cloud/MES 死信记录。",
            AvaloniaRuntimeStatus.StartFailed => "已尝试 --start-runtime，但启动失败；应保留诊断信息，不继续 I/O 写入申请。",
            AvaloniaRuntimeStatus.Stopping => "运行链路正在停机；诊断页只读展示最后状态。",
            _ => "运行链路状态未知；诊断页只读展示当前快照。"
        };

        return JoinSummary(
            modeMessage,
            string.IsNullOrWhiteSpace(snapshot.DetailText) ? null : $"状态详情：{snapshot.DetailText}",
            string.IsNullOrWhiteSpace(snapshot.DiagnosticsSummary) ? null : $"启动摘要：{snapshot.DiagnosticsSummary}",
            string.IsNullOrWhiteSpace(snapshot.DiagnosticsLogPath) ? null : $"诊断日志：{snapshot.DiagnosticsLogPath}");
    }

    internal static string BuildPlcTraceSummary(PlcIoWriteTraceEntry trace)
    {
        var signalText = trace.SignalKeys.Count == 0
            ? "--"
            : string.Join("、", trace.SignalKeys);

        return JoinSummary(
            $"时间：{FormatTime(trace.OccurredAt)}",
            $"设备：{trace.DeviceName}",
            $"块：{trace.StartAddress} / {trace.WordCount} 字",
            $"信号：{signalText}",
            string.IsNullOrWhiteSpace(trace.ErrorMessage) ? null : $"原因：{trace.ErrorMessage}");
    }

    internal static string BuildCloudReadonlySummary(CloudSyncDiagnosticsSnapshot cloud)
    {
        var pendingCount = cloud.PendingRetryCount
            + cloud.PendingPassStationCount
            + cloud.PendingDeviceLogCount
            + cloud.PendingCapacityCount;

        return JoinSummary(
            $"运行：{FormatCloudRuntimeState(cloud.RuntimeState)}",
            $"闸门：{FormatCloudGateState(cloud.GateState)}",
            $"待补传：{pendingCount}",
            $"死信：{cloud.DeadLetters?.TotalCount ?? 0}",
            $"最近结果：{FormatCloudOutcome(cloud.LastOutcome)}",
            cloud.IsPersistenceFaulted ? $"持久化故障：{cloud.PersistenceFaultMessage}" : null);
    }

    internal static string BuildMesReadonlySummary(MesSyncDiagnosticsSnapshot mes)
        => JoinSummary(
            $"运行：{FormatMesRuntimeState(mes.RuntimeState)}",
            $"待补传：{mes.PendingRetryCount}",
            $"死信：{mes.DeadLetters?.TotalCount ?? 0}",
            $"最近失败：{FormatTime(mes.LastFailureAt)}",
            mes.IsPersistenceFaulted ? $"持久化故障：{mes.PersistenceFaultMessage}" : null);

    internal static string FormatTraceKind(PlcIoWriteTraceKind kind)
        => kind switch
        {
            PlcIoWriteTraceKind.Attempt => "尝试",
            PlcIoWriteTraceKind.Success => "成功",
            PlcIoWriteTraceKind.Failed => "失败",
            _ => kind.ToString()
        };

    internal static string FormatIoWriteKind(IoViewWriteResultKind kind)
        => kind switch
        {
            IoViewWriteResultKind.AcceptedToRuntimeBuffer => "已进入运行时缓冲",
            IoViewWriteResultKind.RuntimeNotStarted => "运行链路未启动",
            IoViewWriteResultKind.NoPermission => "权限不足",
            IoViewWriteResultKind.DeviceNotBound => "设备未绑定",
            IoViewWriteResultKind.PlcDisconnected => "PLC 未连接",
            IoViewWriteResultKind.NoWritableSignal => "无可写信号",
            IoViewWriteResultKind.InvalidValue => "写入值无效",
            IoViewWriteResultKind.RejectedByUser => "用户取消",
            IoViewWriteResultKind.BufferUnavailable => "运行时缓冲不可用",
            _ => kind.ToString()
        };

    internal static string FormatCloudDiagnosticStatus(CloudSyncDiagnosticStatus status)
        => status switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => "存储故障",
            CloudSyncDiagnosticStatus.CapacityBlocked => "产能阻塞",
            CloudSyncDiagnosticStatus.WaitingHeartbeat => "等待心跳",
            CloudSyncDiagnosticStatus.Ready => "已就绪",
            CloudSyncDiagnosticStatus.WaitingRecovery => "等待恢复",
            CloudSyncDiagnosticStatus.Blocked => "已阻断",
            _ => status.ToString()
        };

    internal static string FormatMesDiagnosticStatus(MesSyncDiagnosticStatus status)
        => status switch
        {
            MesSyncDiagnosticStatus.PersistenceFaulted => "存储故障",
            MesSyncDiagnosticStatus.CapacityBlocked => "产能阻塞",
            MesSyncDiagnosticStatus.WaitingHeartbeat => "等待心跳",
            MesSyncDiagnosticStatus.Retrying => "重试中",
            MesSyncDiagnosticStatus.Backoff => "退避中",
            MesSyncDiagnosticStatus.LastFailed => "最近失败",
            MesSyncDiagnosticStatus.Idle => "空闲",
            _ => status.ToString()
        };

    internal static string FormatCloudRuntimeState(CloudRetryRuntimeState state)
        => state switch
        {
            CloudRetryRuntimeState.Idle => "空闲",
            CloudRetryRuntimeState.Retrying => "重试中",
            CloudRetryRuntimeState.Backoff => "退避中",
            CloudRetryRuntimeState.WaitingForRecovery => "等待恢复",
            _ => state.ToString()
        };

    internal static string FormatMesRuntimeState(MesRetryRuntimeState state)
        => state switch
        {
            MesRetryRuntimeState.Idle => "空闲",
            MesRetryRuntimeState.Retrying => "重试中",
            MesRetryRuntimeState.Backoff => "退避中",
            MesRetryRuntimeState.LastFailed => "最近失败",
            _ => state.ToString()
        };

    internal static string FormatCloudGateState(EdgeUploadGateState state)
        => state switch
        {
            EdgeUploadGateState.Unknown => "未知",
            EdgeUploadGateState.Refreshing => "刷新中",
            EdgeUploadGateState.Ready => "已就绪",
            EdgeUploadGateState.Blocked => "已阻断",
            _ => state.ToString()
        };

    internal static string FormatCloudOutcome(CloudCallOutcome outcome)
        => outcome switch
        {
            CloudCallOutcome.Success => "成功",
            CloudCallOutcome.SkippedUploadNotReady => "上传闸门未就绪",
            CloudCallOutcome.UnauthorizedAfterRetry => "鉴权失败",
            CloudCallOutcome.HttpFailure => "HTTP 失败",
            CloudCallOutcome.NetworkFailure => "网络失败",
            CloudCallOutcome.Exception => "异常",
            _ => outcome.ToString()
        };

    internal static string JoinSummary(params string?[] segments)
        => string.Join("；", segments.Where(static segment => !string.IsNullOrWhiteSpace(segment)));

}
