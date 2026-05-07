using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

internal sealed class DiagnosticsSummaryBuilder(
    IAppLanguageService languageService,
    LocalizedSyncDiagnosticsText diagnosticsText,
    DiagnosticsModuleDisplayNameResolver displayNameResolver)
{
    public DiagnosticsSummarySnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        return new DiagnosticsSummarySnapshot
        {
            DiscoveredModulesSummary = BuildModuleSummary(
                GetText("Navigation_Diagnostics_DiscoveredLabel", "已发现模块"),
                GetText("Navigation_Diagnostics_NoDiscoveredModules", "当前未发现模块。"),
                report.DiscoveredModules,
                moduleNameMap),
            EnabledModulesSummary = BuildModuleSummary(
                GetText("Navigation_Diagnostics_EnabledLabel", "已启用模块"),
                GetText("Navigation_Diagnostics_NoEnabledModules", "当前没有配置为启用的模块。"),
                report.EnabledModules,
                moduleNameMap),
            ActivatedModulesSummary = BuildModuleSummary(
                GetText("Navigation_Diagnostics_ActivatedLabel", "已激活模块"),
                GetText("Navigation_Diagnostics_NoActivatedModules", "当前没有已激活模块。"),
                report.ActivatedModules,
                moduleNameMap),
            ConfigurationProfileSummary = BuildConfigurationProfileSummary(report.ConfigurationProfile),
            LastUpdatedSummary = report.GeneratedAt == DateTime.MinValue
                ? GetText("Navigation_Diagnostics_StartupPending", "启动诊断尚未生成。")
                : FormatText("Navigation_Diagnostics_LastGeneratedFormat", "最近生成：{0:yyyy-MM-dd HH:mm:ss}", report.GeneratedAt),
            DeviceSummary = FormatText("Navigation_Diagnostics_DeviceFormat", "设备：{0}", syncDiagnostics.DeviceName),
            CloudGateSummary = FormatText("Navigation_Sync_UploadGateFormat", "上传门禁：{0}", BuildCloudGate(syncDiagnostics.Cloud)),
            CloudRuntimeSummary = FormatText(
                "Navigation_Diagnostics_CloudRuntimeFormat",
                "云端运行：{0}",
                diagnosticsText.FormatCloudRuntimeState(syncDiagnostics.Cloud.RuntimeState)),
            CloudResultSummary = FormatText(
                "Navigation_Sync_LastResultFormat",
                "最近结果：{0}",
                diagnosticsText.FormatCloudOutcome(
                    syncDiagnostics.Cloud.LastOutcome,
                    syncDiagnostics.Cloud.LastReasonCode,
                    syncDiagnostics.Cloud.LastProcessType,
                    syncDiagnostics.Cloud.LastProcessDisplayName)),
            CloudPendingSummary = BuildCloudPendingSummary(syncDiagnostics.Cloud),
            CloudCapacitySummary = diagnosticsText.FormatCapacityBlockedSummary(
                syncDiagnostics.Cloud.IsCapacityBlocked,
                syncDiagnostics.Cloud.BlockedChannel,
                syncDiagnostics.Cloud.BlockedReason,
                syncDiagnostics.Cloud.LastCapacityBlockAt),
            CloudPersistenceSummary = diagnosticsText.FormatPersistenceFaultSummary(
                syncDiagnostics.Cloud.IsPersistenceFaulted,
                syncDiagnostics.Cloud.LastPersistenceFaultAt,
                syncDiagnostics.Cloud.PersistenceFaultMessage),
            CloudLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastAttemptAt)),
            CloudLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastSuccessAt)),
            CloudLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastFailureAt)),
            MesRuntimeSummary = FormatText(
                "Navigation_Diagnostics_MesRuntimeFormat",
                "MES运行：{0}",
                diagnosticsText.FormatMesRuntimeState(syncDiagnostics.Mes.RuntimeState)),
            MesPendingSummary = BuildMesPendingSummary(syncDiagnostics.Mes),
            MesCapacitySummary = diagnosticsText.FormatCapacityBlockedSummary(
                syncDiagnostics.Mes.IsCapacityBlocked,
                syncDiagnostics.Mes.BlockedChannel,
                syncDiagnostics.Mes.BlockedReason,
                syncDiagnostics.Mes.LastCapacityBlockAt),
            MesPersistenceSummary = diagnosticsText.FormatPersistenceFaultSummary(
                syncDiagnostics.Mes.IsPersistenceFaulted,
                syncDiagnostics.Mes.LastPersistenceFaultAt,
                syncDiagnostics.Mes.PersistenceFaultMessage),
            MesLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastAttemptAt)),
            MesLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastSuccessAt)),
            MesLastFailureSummary = FormatText(
                "Navigation_Sync_LastFailureWithReasonFormat",
                "最近失败：{0}（{1}）",
                diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastFailureAt),
                DiagnosticsTextNormalizer.Normalize(syncDiagnostics.Mes.LastFailureReason)),
            ContextPersistenceSummary = diagnosticsText.FormatContextPersistenceSummary(syncDiagnostics.ContextPersistence),
            StartupStatusMessage = BuildStartupStatus(report)
        };
    }

    private string BuildCloudGate(CloudSyncDiagnosticsSnapshot cloud)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(cloud) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => GetText("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            CloudSyncDiagnosticStatus.CapacityBlocked => GetText("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            CloudSyncDiagnosticStatus.WaitingHeartbeat => GetText("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            CloudSyncDiagnosticStatus.Ready => GetText("Navigation_Sync_StatusReady", "已就绪"),
            CloudSyncDiagnosticStatus.WaitingRecovery => GetText("Navigation_Sync_StatusWaitingRecovery", "等待恢复"),
            _ => FormatText("Navigation_Sync_StatusBlockedFormat", "已阻塞（{0}）", diagnosticsText.FormatBlockReason(cloud.BlockReason))
        };

    private string BuildCloudPendingSummary(CloudSyncDiagnosticsSnapshot cloud)
        => FormatText(
            "Navigation_Sync_PendingCloudFormat",
            "待处理：重试={0}，日志={1}，产能={2}",
            cloud.PendingRetryCount,
            cloud.PendingDeviceLogCount,
            cloud.PendingCapacityCount)
        + FormatText(
            "Navigation_Sync_DeadLetterSuffixFormat",
            "，死信={0}",
            cloud.DeadLetters?.TotalCount ?? 0);

    private string BuildMesPendingSummary(MesSyncDiagnosticsSnapshot mes)
        => FormatText("Navigation_Sync_PendingMesFormat", "待处理：重试={0}", mes.PendingRetryCount)
        + FormatText(
            "Navigation_Sync_DeadLetterSuffixFormat",
            "，死信={0}",
            mes.DeadLetters?.TotalCount ?? 0);

    private string BuildModuleSummary(
        string label,
        string emptyText,
        IReadOnlyList<string> modules,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (modules.Count == 0)
        {
            return emptyText;
        }

        var names = modules
            .Select(x => displayNameResolver.ResolveModuleDisplayName(x, moduleNameMap))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return FormatText("Navigation_Diagnostics_ModuleSummaryFormat", "{0}：{1}", label, string.Join(GetText("Navigation_ListSeparator", "、"), names));
    }

    private string BuildConfigurationProfileSummary(ConfigurationProfileSnapshot profile)
    {
        if (string.IsNullOrWhiteSpace(profile.MachineProfile))
        {
            return FormatText(
                "Navigation_Diagnostics_ProfileNoMachineFormat",
                "环境：{0}；机型：未配置；运行目录：{1}",
                profile.EnvironmentName,
                profile.RuntimeDataRoot);
        }

        var profileFileName = DiagnosticsTextNormalizer.Normalize(profile.MachineProfileFileName);
        var loadState = profile.IsMachineProfileLoaded
            ? FormatText("Navigation_Diagnostics_ProfileLoadedFormat", "已从 {0} 加载", profileFileName)
            : FormatText("Navigation_Diagnostics_ProfileMissingFormat", "未找到 {0}", profileFileName);
        return FormatText(
            "Navigation_Diagnostics_ProfileWithMachineFormat",
            "环境：{0}；机型：{1}（{2}）；运行目录：{3}",
            profile.EnvironmentName,
            profile.MachineProfile,
            loadState,
            profile.RuntimeDataRoot);
    }

    private string BuildStartupStatus(StartupDiagnosticsReport report)
        => report.Issues.Count == 0
            ? GetText("Navigation_Diagnostics_StartupOk", "启动诊断正常。")
            : FormatText("Navigation_Diagnostics_StartupIssueCountFormat", "启动诊断发现 {0} 个问题。", report.Issues.Count);

    private string GetText(string key, string fallback)
        => languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => languageService.Format(key, fallback, args);
}
