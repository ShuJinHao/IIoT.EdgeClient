using IIoT.Edge.Application.Context;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsInitialSummaryFactory
{
    DiagnosticsSummarySnapshot Create();
}

internal sealed class DiagnosticsInitialSummaryFactory(
    IAppLanguageService languageService,
    LocalizedSyncDiagnosticsText diagnosticsText)
    : IDiagnosticsInitialSummaryFactory
{
    public DiagnosticsSummarySnapshot Create()
        => new()
        {
            DiscoveredModulesSummary = GetText("Navigation_Diagnostics_DiscoveredChecking", "正在检查已发现模块..."),
            EnabledModulesSummary = GetText("Navigation_Diagnostics_EnabledChecking", "正在检查已启用模块..."),
            ActivatedModulesSummary = GetText("Navigation_Diagnostics_ActivatedChecking", "正在检查已激活模块..."),
            ConfigurationProfileSummary = GetText("Navigation_Diagnostics_ConfigPlaceholder", "配置概况：-"),
            LastUpdatedSummary = GetText("Navigation_Diagnostics_StartupPending", "启动诊断尚未生成。"),
            DeviceSummary = FormatText("Navigation_Diagnostics_DeviceFormat", "设备：{0}", GetText("Navigation_Unknown", "未知")),
            CloudGateSummary = FormatText("Navigation_Sync_UploadGateFormat", "上传门禁：{0}", "--"),
            CloudRuntimeSummary = FormatText("Navigation_Diagnostics_CloudRuntimeFormat", "云端运行：{0}", GetText("Navigation_Runtime_Idle", "空闲")),
            CloudResultSummary = FormatText("Navigation_Sync_LastResultFormat", "最近结果：{0}", "--"),
            CloudPendingSummary = FormatText("Navigation_Sync_PendingCloudFormat", "待处理：过站={0}，日志={1}，产能={2}", 0, 0, 0),
            CloudCapacitySummary = diagnosticsText.FormatCapacityBlockedSummary(false, null, string.Empty, null),
            CloudPersistenceSummary = diagnosticsText.FormatPersistenceFaultSummary(false, null, null),
            CloudLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", "--"),
            CloudLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", "--"),
            CloudLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", "--"),
            MesRuntimeSummary = FormatText("Navigation_Diagnostics_MesRuntimeFormat", "MES运行：{0}", GetText("Navigation_Runtime_Idle", "空闲")),
            MesPendingSummary = FormatText("Navigation_Sync_PendingMesFormat", "待处理：重试={0}", 0),
            MesCapacitySummary = diagnosticsText.FormatCapacityBlockedSummary(false, null, string.Empty, null),
            MesPersistenceSummary = diagnosticsText.FormatPersistenceFaultSummary(false, null, null),
            MesLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", "--"),
            MesLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", "--"),
            MesLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", "--"),
            ContextPersistenceSummary = diagnosticsText.FormatContextPersistenceSummary(new ProductionContextPersistenceDiagnostics(0, null))
        };

    private string GetText(string key, string fallback)
        => languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => languageService.Format(key, fallback, args);
}

