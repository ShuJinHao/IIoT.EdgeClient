using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsRowsBuilder
{
    DiagnosticsRowsSnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyDictionary<string, string> moduleNameMap);
}

internal sealed class DiagnosticsRowsBuilder(
    IAppLanguageService languageService,
    LocalizedSyncDiagnosticsText diagnosticsText,
    IDiagnosticsModuleDisplayNameResolver displayNameResolver)
    : IDiagnosticsRowsBuilder
{
    public DiagnosticsRowsSnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        var cloudDeadLetters = BuildDeadLetters(DataPipelineRetryChannel.Cloud, syncDiagnostics.Cloud.DeadLetters?.LatestRecords);
        var mesDeadLetters = BuildDeadLetters(DataPipelineRetryChannel.Mes, syncDiagnostics.Mes.DeadLetters?.LatestRecords);

        return new DiagnosticsRowsSnapshot(
            BuildModuleRegistrations(report, moduleNameMap),
            BuildPluginStates(report),
            BuildDeviceBindings(report),
            BuildIssues(report),
            BuildMesUploadDiagnostics(syncDiagnostics),
            BuildSyncChannels(syncDiagnostics),
            cloudDeadLetters,
            mesDeadLetters);
    }

    private IReadOnlyList<ModuleRegistrationRow> BuildModuleRegistrations(
        StartupDiagnosticsReport report,
        IReadOnlyDictionary<string, string> moduleNameMap)
        => report.ModuleRegistrations
            .Select(x => new ModuleRegistrationRow(
                x.ModuleId,
                displayNameResolver.ResolveProcessDisplayName(x.ModuleId, x.ProcessType, moduleNameMap),
                x.AssemblyName,
                x.IsEnabled,
                x.HasCellDataRegistration,
                x.HasRuntimeFactory,
                x.HasCloudUploader,
                x.HasMesUploader,
                x.HasHardwareProfile))
            .ToArray();

    private IReadOnlyList<PluginLifecycleRow> BuildPluginStates(StartupDiagnosticsReport report)
        => report.PluginStates
            .Select(x => new PluginLifecycleRow(
                x.ModuleId,
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, x.DisplayName),
                diagnosticsText.FormatProcessType(x.ProcessType),
                x.Version,
                diagnosticsText.FormatPluginLifecycleState(x.State),
                DiagnosticsTextNormalizer.Normalize(x.Message)))
            .ToArray();

    private static IReadOnlyList<DeviceModuleBindingRow> BuildDeviceBindings(StartupDiagnosticsReport report)
        => report.DeviceBindings
            .Select(x => new DeviceModuleBindingRow(
                x.DeviceName,
                DiagnosticsTextNormalizer.Normalize(x.ModuleId),
                x.ModuleExists,
                x.ModuleEnabled,
                x.HasIoMappings))
            .ToArray();

    private static IReadOnlyList<StartupDiagnosticIssueRow> BuildIssues(StartupDiagnosticsReport report)
        => report.Issues
            .Select(x => new StartupDiagnosticIssueRow(
                x.Code,
                DiagnosticsTextNormalizer.Normalize(x.ModuleId),
                DiagnosticsTextNormalizer.Normalize(x.DeviceName),
                DiagnosticsTextNormalizer.Normalize(x.Message)))
            .ToArray();

    private IReadOnlyList<MesChannelDiagnosticsRow> BuildMesUploadDiagnostics(
        EdgeSyncDiagnosticsSnapshot syncDiagnostics)
        => syncDiagnostics.Mes.Channels
            .Select(x => new MesChannelDiagnosticsRow(
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, x.ProcessDisplayName),
                diagnosticsText.FormatMesChannelResult(x.LastResult),
                diagnosticsText.FormatTimestamp(x.LastAttemptAt),
                diagnosticsText.FormatTimestamp(x.LastSuccessAt),
                DiagnosticsTextNormalizer.Normalize(x.LastFailureReason)))
            .ToArray();

    private IReadOnlyList<SyncChannelRow> BuildSyncChannels(EdgeSyncDiagnosticsSnapshot syncDiagnostics)
        =>
        [
            new(
                GetText("Navigation_Diagnostics_ChannelCloud", "云端"),
                BuildCloudStatus(syncDiagnostics.Cloud),
                FormatText(
                    "Navigation_Diagnostics_SyncCloudPendingFormat",
                    "过站={0}，日志={1}，产能={2}",
                    syncDiagnostics.Cloud.PendingPassStationCount,
                    syncDiagnostics.Cloud.PendingDeviceLogCount,
                    syncDiagnostics.Cloud.PendingCapacityCount),
                syncDiagnostics.Cloud.DeadLetters?.TotalCount ?? 0,
                BuildCloudLastError(syncDiagnostics.Cloud),
                BuildCloudNote(syncDiagnostics.Cloud)),
            new(
                GetText("Navigation_Diagnostics_ChannelMes", "MES"),
                BuildMesStatus(syncDiagnostics.Mes),
                FormatText("Navigation_Diagnostics_SyncMesPendingFormat", "重试={0}", syncDiagnostics.Mes.PendingRetryCount),
                syncDiagnostics.Mes.DeadLetters?.TotalCount ?? 0,
                BuildMesLastError(syncDiagnostics.Mes),
                BuildMesNote(syncDiagnostics.Mes))
        ];

    private string BuildCloudStatus(CloudSyncDiagnosticsSnapshot cloud)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(cloud) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => GetText("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            CloudSyncDiagnosticStatus.CapacityBlocked => GetText("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            CloudSyncDiagnosticStatus.WaitingHeartbeat => GetText("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            CloudSyncDiagnosticStatus.Ready => GetText("Navigation_Sync_StatusReady", "已就绪"),
            CloudSyncDiagnosticStatus.WaitingRecovery => GetText("Navigation_Sync_StatusWaitingRecovery", "等待恢复"),
            _ => FormatText("Navigation_Sync_StatusBlockedFormat", "已阻塞（{0}）", diagnosticsText.FormatBlockReason(cloud.BlockReason))
        };

    private string BuildMesStatus(MesSyncDiagnosticsSnapshot mes)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyMes(mes) switch
        {
            MesSyncDiagnosticStatus.PersistenceFaulted => GetText("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            MesSyncDiagnosticStatus.CapacityBlocked => GetText("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            MesSyncDiagnosticStatus.WaitingHeartbeat => GetText("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            _ => diagnosticsText.FormatMesRuntimeState(mes.RuntimeState)
        };

    private string BuildCloudLastError(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (cloud.LastOutcome == CloudCallOutcome.Success &&
            (string.IsNullOrWhiteSpace(cloud.LastReasonCode) ||
             string.Equals(cloud.LastReasonCode, "none", StringComparison.OrdinalIgnoreCase)))
        {
            return "--";
        }

        return diagnosticsText.FormatCloudOutcome(
            cloud.LastOutcome,
            cloud.LastReasonCode,
            cloud.LastProcessType,
            cloud.LastProcessDisplayName);
    }

    private static string BuildMesLastError(MesSyncDiagnosticsSnapshot mes)
        => DiagnosticsTextNormalizer.Normalize(mes.LastFailureReason);

    private string BuildCloudNote(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (cloud.IsPersistenceFaulted)
        {
            return diagnosticsText.FormatPersistenceFaultSummary(
                cloud.IsPersistenceFaulted,
                cloud.LastPersistenceFaultAt,
                cloud.PersistenceFaultMessage);
        }

        if (cloud.IsCapacityBlocked)
        {
            return diagnosticsText.FormatCapacityBlockedSummary(
                cloud.IsCapacityBlocked,
                cloud.BlockedChannel,
                cloud.BlockedReason,
                cloud.LastCapacityBlockAt);
        }

        if (cloud.Heartbeat is { IsReady: false })
        {
            return diagnosticsText.FormatHeartbeatSummary(cloud.Heartbeat);
        }

        if (cloud.IsPausedWaitingForRecovery)
        {
            return GetText("Navigation_Sync_StatusWaitingRecovery", "等待恢复");
        }

        if (cloud.BlockReason != EdgeUploadBlockReason.None)
        {
            return FormatText(
                "Navigation_Diagnostics_SyncBlockedReasonFormat",
                "阻塞：{0}",
                diagnosticsText.FormatBlockReason(cloud.BlockReason));
        }

        return "--";
    }

    private string BuildMesNote(MesSyncDiagnosticsSnapshot mes)
    {
        if (mes.IsPersistenceFaulted)
        {
            return diagnosticsText.FormatPersistenceFaultSummary(
                mes.IsPersistenceFaulted,
                mes.LastPersistenceFaultAt,
                mes.PersistenceFaultMessage);
        }

        if (mes.IsCapacityBlocked)
        {
            return diagnosticsText.FormatCapacityBlockedSummary(
                mes.IsCapacityBlocked,
                mes.BlockedChannel,
                mes.BlockedReason,
                mes.LastCapacityBlockAt);
        }

        if (mes.Heartbeat is { IsReady: false })
        {
            return diagnosticsText.FormatHeartbeatSummary(mes.Heartbeat);
        }

        return "--";
    }

    private IReadOnlyList<DeadLetterRow> BuildDeadLetters(
        DataPipelineRetryChannel channel,
        IReadOnlyList<DeadLetterRecord>? records)
        => (records ?? [])
            .Select(x => DeadLetterRow.From(
                channel,
                x,
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, null),
                diagnosticsText.FormatTimestamp(x.CreatedAt)))
            .ToArray();

    private string GetText(string key, string fallback)
        => languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => languageService.Format(key, fallback, args);
}
