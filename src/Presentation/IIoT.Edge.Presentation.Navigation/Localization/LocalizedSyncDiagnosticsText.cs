using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.UI.Shared.Localization;

using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Shared;
namespace IIoT.Edge.Presentation.Navigation.Localization;

internal sealed class LocalizedSyncDiagnosticsText(
    IAppLanguageService languageService,
    IProductionTimeProvider? productionTime = null)
{
    public string FormatCloudMonitorSummary(CloudSyncDiagnosticsSnapshot snapshot)
    {
        var gateText = EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(snapshot) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => Text("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            CloudSyncDiagnosticStatus.CapacityBlocked => Text("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            CloudSyncDiagnosticStatus.WaitingHeartbeat => Text("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            CloudSyncDiagnosticStatus.Ready => Text("Navigation_Sync_StatusReady", "已就绪"),
            CloudSyncDiagnosticStatus.WaitingRecovery => Text("Navigation_Sync_StatusWaitingRecovery", "等待恢复"),
            _ => Format("Navigation_Sync_StatusBlockedFormat", "已阻塞（{0}）", FormatBlockReason(snapshot.BlockReason))
        };

        return string.Join(Environment.NewLine, [
            Format("Navigation_Sync_UploadGateFormat", "上传门禁：{0}", gateText),
            Format("Navigation_Sync_RuntimeFormat", "运行状态：{0}", FormatCloudRuntimeState(snapshot.RuntimeState)),
            Format(
                "Navigation_Sync_LastResultFormat",
                "最近结果：{0}",
                FormatCloudOutcome(
                    snapshot.LastOutcome,
                    snapshot.LastReasonCode,
                    snapshot.LastProcessType,
                    snapshot.LastProcessDisplayName)),
            Format("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", FormatTimestamp(snapshot.LastSuccessAt)),
            Format("Navigation_Sync_LastFailureFormat", "最近失败：{0}", FormatTimestamp(snapshot.LastFailureAt)),
            Format(
                "Navigation_Sync_PendingCloudFormat",
                "待处理：过站={0}，日志={1}，产能={2}",
                snapshot.PendingPassStationCount,
                snapshot.PendingDeviceLogCount,
                snapshot.PendingCapacityCount),
            FormatHeartbeatSummary(snapshot.Heartbeat),
            FormatPersistenceFaultSummary(
                snapshot.IsPersistenceFaulted,
                snapshot.LastPersistenceFaultAt,
                snapshot.PersistenceFaultMessage),
            FormatCapacityBlockedSummary(
                snapshot.IsCapacityBlocked,
                snapshot.BlockedChannel,
                snapshot.BlockedReason,
                snapshot.LastCapacityBlockAt)
        ]);
    }

    public string FormatMesMonitorSummary(MesSyncDiagnosticsSnapshot snapshot)
        => string.Join(Environment.NewLine, [
            Format("Navigation_Sync_RuntimeFormat", "运行状态：{0}", FormatMesRuntimeState(snapshot.RuntimeState)),
            Format("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", FormatTimestamp(snapshot.LastAttemptAt)),
            Format("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", FormatTimestamp(snapshot.LastSuccessAt)),
            Format("Navigation_Sync_LastFailureFormat", "最近失败：{0}", FormatTimestamp(snapshot.LastFailureAt)),
            Format("Navigation_Sync_FailureReasonFormat", "失败原因：{0}", NormalizeText(snapshot.LastFailureReason)),
            Format("Navigation_Sync_PendingMesFormat", "待处理：重试={0}", snapshot.PendingRetryCount),
            FormatHeartbeatSummary(snapshot.Heartbeat),
            FormatPersistenceFaultSummary(
                snapshot.IsPersistenceFaulted,
                snapshot.LastPersistenceFaultAt,
                snapshot.PersistenceFaultMessage),
            FormatCapacityBlockedSummary(
                snapshot.IsCapacityBlocked,
                snapshot.BlockedChannel,
                snapshot.BlockedReason,
                snapshot.LastCapacityBlockAt)
        ]);

    public string FormatHeartbeatSummary(ExternalHeartbeatSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return Text("Navigation_Heartbeat_NotConfigured", "心跳：未配置");
        }

        var stateText = snapshot.State switch
        {
            ExternalHeartbeatState.Ready => Text("Navigation_Heartbeat_Ready", "心跳：已就绪"),
            ExternalHeartbeatState.NotReady => Format(
                "Navigation_Heartbeat_NotReadyFormat",
                "心跳：等待恢复（{0}）",
                NormalizeText(snapshot.ReasonCode)),
            _ => Text("Navigation_Heartbeat_Unknown", "心跳：未知")
        };

        return stateText;
    }

    public string FormatPersistenceFaultSummary(
        bool isPersistenceFaulted,
        DateTime? lastPersistenceFaultAt,
        string? persistenceFaultMessage)
    {
        if (!isPersistenceFaulted)
        {
            return Text("Navigation_Sync_PersistenceFaultNo", "存储故障：否");
        }

        return Format(
            "Navigation_Sync_PersistenceFaultYesFormat",
            "存储故障：是，最近 {0}，原因：{1}",
            FormatTimestamp(lastPersistenceFaultAt),
            NormalizeText(persistenceFaultMessage));
    }

    public string FormatCapacityBlockedSummary(
        bool isCapacityBlocked,
        CapacityBlockedChannel? blockedChannel,
        string blockedReason,
        DateTime? lastCapacityBlockAt)
    {
        if (!isCapacityBlocked)
        {
            return Text("Navigation_Sync_CapacityBlockedNo", "产能阻塞：否");
        }

        return Format(
            "Navigation_Sync_CapacityBlockedYesFormat",
            "产能阻塞：是（{0} / {1}），最近 {2}",
            FormatBlockedChannel(blockedChannel),
            FormatCapacityBlockedReason(blockedReason),
            FormatTimestamp(lastCapacityBlockAt));
    }

    public string FormatContextPersistenceSummary(ProductionContextPersistenceDiagnostics diagnostics)
        => string.Join(Environment.NewLine, [
            Format("Navigation_Sync_ContextCorruptCountFormat", "损坏文件数：{0}", diagnostics.CorruptFileCount),
            Format("Navigation_Sync_ContextLastCorruptFormat", "最近损坏文件：{0}", FormatTimestamp(diagnostics.LastCorruptDetectedAt))
        ]);

    public string FormatBlockReason(EdgeUploadBlockReason reason) => reason switch
    {
        EdgeUploadBlockReason.None => Text("Navigation_BlockReason_None", "无"),
        EdgeUploadBlockReason.DeviceUnidentified => Text("Navigation_BlockReason_DeviceUnidentified", "设备未识别"),
        EdgeUploadBlockReason.MissingUploadToken => Text("Navigation_BlockReason_MissingUploadToken", "缺少上传令牌"),
        EdgeUploadBlockReason.ExpiredUploadToken => Text("Navigation_BlockReason_ExpiredUploadToken", "上传令牌已过期"),
        EdgeUploadBlockReason.BootstrapHttpFailure => Text("Navigation_BlockReason_BootstrapHttpFailure", "bootstrap HTTP 失败"),
        EdgeUploadBlockReason.BootstrapTimeout => Text("Navigation_BlockReason_BootstrapTimeout", "bootstrap 超时"),
        EdgeUploadBlockReason.BootstrapNetworkFailure => Text("Navigation_BlockReason_BootstrapNetworkFailure", "bootstrap 网络失败"),
        EdgeUploadBlockReason.BootstrapPayloadInvalid => Text("Navigation_BlockReason_BootstrapPayloadInvalid", "bootstrap 响应无效"),
        EdgeUploadBlockReason.UploadTokenRejected => Text("Navigation_BlockReason_UploadTokenRejected", "上传令牌被拒绝"),
        EdgeUploadBlockReason.CloudUploadDisabled => Text("Navigation_BlockReason_CloudUploadDisabled", "云端上传已关闭"),
        _ => Text("Navigation_Unknown", "未知")
    };

    public string FormatCloudRuntimeState(CloudRetryRuntimeState state) => state switch
    {
        CloudRetryRuntimeState.Idle => Text("Navigation_Runtime_Idle", "空闲"),
        CloudRetryRuntimeState.Retrying => Text("Navigation_Runtime_Retrying", "重试中"),
        CloudRetryRuntimeState.Backoff => Text("Navigation_Runtime_Backoff", "退避中"),
        CloudRetryRuntimeState.WaitingForRecovery => Text("Navigation_Runtime_WaitingRecovery", "等待恢复"),
        _ => Text("Navigation_Unknown", "未知")
    };

    public string FormatMesRuntimeState(MesRetryRuntimeState state) => state switch
    {
        MesRetryRuntimeState.Idle => Text("Navigation_Runtime_Idle", "空闲"),
        MesRetryRuntimeState.Retrying => Text("Navigation_Runtime_Retrying", "重试中"),
        MesRetryRuntimeState.Backoff => Text("Navigation_Runtime_Backoff", "退避中"),
        MesRetryRuntimeState.LastFailed => Text("Navigation_Runtime_LastFailed", "最近失败"),
        _ => Text("Navigation_Unknown", "未知")
    };

    public string FormatCloudOutcome(
        CloudCallOutcome outcome,
        string reasonCode,
        string? processType,
        string? processDisplayName = null)
    {
        var outcomeText = outcome switch
        {
            CloudCallOutcome.Success => Text("Navigation_Outcome_Success", "成功"),
            CloudCallOutcome.SkippedUploadNotReady => Text("Navigation_Outcome_SkippedUploadNotReady", "未就绪，已跳过"),
            CloudCallOutcome.UnauthorizedAfterRetry => Text("Navigation_Outcome_UnauthorizedAfterRetry", "重试后仍未授权"),
            CloudCallOutcome.HttpFailure => Text("Navigation_Outcome_HttpFailure", "HTTP 失败"),
            CloudCallOutcome.NetworkFailure => Text("Navigation_Outcome_NetworkFailure", "网络失败"),
            CloudCallOutcome.Exception => Text("Navigation_Outcome_Exception", "异常"),
            _ => Text("Navigation_Unknown", "未知")
        };

        return Format(
            "Navigation_Outcome_ProcessReasonFormat",
            "{0}（{1} / {2}）",
            outcomeText,
            string.IsNullOrWhiteSpace(processDisplayName) ? FormatProcessType(processType) : processDisplayName,
            NormalizeText(reasonCode));
    }

    public string FormatMesChannelResult(string? lastResult) => lastResult switch
    {
        null or "" => "--",
        "Success" => Text("Navigation_Outcome_Success", "成功"),
        "Failed" => Text("Navigation_Outcome_Failed", "失败"),
        "Blocked" => Text("Navigation_Outcome_Blocked", "已阻塞"),
        _ => lastResult
    };

    public string FormatPluginLifecycleState(PluginLifecycleState state) => state switch
    {
        PluginLifecycleState.Discovered => Text("Navigation_PluginState_Discovered", "已发现"),
        PluginLifecycleState.DisabledByConfig => Text("Navigation_PluginState_DisabledByConfig", "已禁用"),
        PluginLifecycleState.ManifestInvalid => Text("Navigation_PluginState_ManifestInvalid", "清单无效"),
        PluginLifecycleState.DependencyMissing => Text("Navigation_PluginState_DependencyMissing", "依赖缺失"),
        PluginLifecycleState.HostVersionIncompatible => Text("Navigation_PluginState_HostVersionIncompatible", "宿主版本不兼容"),
        PluginLifecycleState.LoadFailed => Text("Navigation_PluginState_LoadFailed", "加载失败"),
        PluginLifecycleState.Activated => Text("Navigation_PluginState_Activated", "已激活"),
        _ => Text("Navigation_Unknown", "未知")
    };

    public string FormatProcessType(string? processType)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            return "--";
        }

        return processType;
    }

    public string FormatTimestamp(DateTime? value)
        => value is null
            ? "--"
            : NormalizeTimestamp(value.Value).ToString("yyyy-MM-dd HH:mm:ss");

    private string FormatBlockedChannel(CapacityBlockedChannel? blockedChannel) => blockedChannel switch
    {
        CapacityBlockedChannel.Retry => Text("Navigation_BlockedChannel_Retry", "重试队列"),
        CapacityBlockedChannel.Fallback => Text("Navigation_BlockedChannel_Fallback", "兜底队列"),
        _ => "--"
    };

    private string FormatCapacityBlockedReason(string blockedReason) => blockedReason switch
    {
        "total" => Text("Navigation_CapacityReason_Total", "总量上限"),
        "process_type" => Text("Navigation_CapacityReason_ProcessType", "工序类型上限"),
        _ => string.IsNullOrWhiteSpace(blockedReason) ? "--" : blockedReason
    };

    private string Text(string key, string fallback)
        => languageService.GetString(key, fallback);

    private string Format(string key, string fallback, params object[] args)
        => languageService.Format(key, fallback, args);

    private DateTime NormalizeTimestamp(DateTime value)
    {
        if (productionTime is not null)
        {
            return productionTime.ToBusinessTime(value);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            DateTimeKind.Local => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            _ => value
        };
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;
}
