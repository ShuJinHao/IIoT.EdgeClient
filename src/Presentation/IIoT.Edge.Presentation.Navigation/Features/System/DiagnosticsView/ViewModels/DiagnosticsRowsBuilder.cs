using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Application.Common.Tasks;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsRowsBuilder
{
    DiagnosticsRowsSnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyList<BackgroundServiceRuntimeSnapshot> backgroundServiceRuntime,
        IReadOnlyDictionary<string, string> moduleNameMap);
}

internal sealed class DiagnosticsRowsBuilder(
    IAppLanguageService languageService,
    LocalizedSyncDiagnosticsText diagnosticsText,
    IDiagnosticsModuleDisplayNameResolver displayNameResolver,
    IDeviceSelectionService deviceSelectionService)
    : IDiagnosticsRowsBuilder
{
    public DiagnosticsRowsSnapshot Build(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyList<BackgroundServiceRuntimeSnapshot> backgroundServiceRuntime,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        var cloudDeadLetters = BuildDeadLetters(DataPipelineRetryChannel.Cloud, syncDiagnostics.Cloud.DeadLetters?.LatestRecords);
        var mesDeadLetters = BuildDeadLetters(DataPipelineRetryChannel.Mes, syncDiagnostics.Mes.DeadLetters?.LatestRecords);
        var issues = BuildIssues(report);
        var visibleIssueCount = report.Issues.Count(x =>
            ShouldIncludeDeviceScopedRow(x.PlcCode, x.DeviceName));

        return new DiagnosticsRowsSnapshot(
            BuildModuleRegistrations(report, moduleNameMap),
            BuildPluginStates(report),
            BuildDeviceBindings(report),
            BuildModuleReadinessRows(report, moduleNameMap),
            issues,
            visibleIssueCount,
            BuildMesUploadDiagnostics(syncDiagnostics),
            BuildSyncChannels(syncDiagnostics, backgroundServiceRuntime),
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

    private IReadOnlyList<DeviceModuleBindingRow> BuildDeviceBindings(StartupDiagnosticsReport report)
        => report.DeviceBindings
            .Where(x => ShouldIncludeDeviceScopedRow(x.PlcCode, x.DeviceName))
            .Select(x => new DeviceModuleBindingRow(
                FormatPlcIdentity(x.PlcCode, x.DeviceName),
                DiagnosticsTextNormalizer.Normalize(x.ModuleId),
                x.ModuleExists,
                x.ModuleEnabled,
                x.HasIoMappings))
            .ToArray();

    private IReadOnlyList<ModuleReadinessRow> BuildModuleReadinessRows(
        StartupDiagnosticsReport report,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        var pluginStates = report.PluginStates
            .GroupBy(x => NormalizeModuleId(x.ModuleId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var registrations = report.ModuleRegistrations
            .GroupBy(x => NormalizeModuleId(x.ModuleId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var bindings = report.DeviceBindings
            .Where(x => ShouldIncludeDeviceScopedRow(x.PlcCode, x.DeviceName))
            .GroupBy(x => NormalizeModuleId(x.ModuleId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var moduleIds = pluginStates.Keys
            .Concat(registrations.Keys)
            .Concat(bindings.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        return moduleIds
            .Select(moduleId =>
            {
                pluginStates.TryGetValue(moduleId, out var plugin);
                registrations.TryGetValue(moduleId, out var registration);
                bindings.TryGetValue(moduleId, out var moduleBindings);

                var processType = plugin?.ProcessType ?? registration?.ProcessType ?? moduleId;
                var displayName = plugin is not null
                    ? displayNameResolver.ResolveProcessDisplayName(plugin.ProcessType, plugin.DisplayName)
                    : displayNameResolver.ResolveProcessDisplayName(moduleId, processType, moduleNameMap);
                var bindingRows = moduleBindings ?? [];
                var deviceNames = bindingRows.Length == 0
                    ? "--"
                    : string.Join(
                        GetText("Navigation_ListSeparator", "、"),
                        bindingRows
                            .Select(x => FormatPlcIdentity(x.PlcCode, x.DeviceName))
                            .Distinct(StringComparer.OrdinalIgnoreCase));

                return new ModuleReadinessRow(
                    moduleId,
                    displayName,
                    diagnosticsText.FormatProcessType(processType),
                    DiagnosticsTextNormalizer.Normalize(plugin?.Version),
                    plugin is null ? "--" : diagnosticsText.FormatPluginLifecycleState(plugin.State),
                    deviceNames,
                    registration is not null,
                    plugin?.State == PluginLifecycleState.Activated,
                    registration?.IsEnabled ?? bindingRows.Any(x => x.ModuleEnabled),
                    registration?.HasRuntimeFactory ?? false,
                    registration?.HasCloudUploader ?? false,
                    registration?.HasMesUploader ?? false,
                    bindingRows.Length == 0 || bindingRows.Any(x => x.HasIoMappings),
                    DiagnosticsTextNormalizer.Normalize(plugin?.Message));
            })
            .ToArray();
    }

    private static string NormalizeModuleId(string? moduleId)
        => DiagnosticsTextNormalizer.Normalize(moduleId);

    private IReadOnlyList<StartupDiagnosticIssueRow> BuildIssues(StartupDiagnosticsReport report)
    {
        var issueRows = report.Issues
            .Where(x => ShouldIncludeDeviceScopedRow(x.PlcCode, x.DeviceName))
            .Select(x => new StartupDiagnosticIssueCandidate(
                NormalizeIssueMessage(DiagnosticsTextNormalizer.Normalize(x.Message)),
                EdgeVisualStatus.Error,
                "ERROR"))
            .ToArray();

        return issueRows
            .GroupBy(x => new { x.Message, x.Status, x.LevelText })
            .Select(group =>
            {
                var row = group.First();
                var duplicateCount = group.Count();

                return new StartupDiagnosticIssueRow(
                    row.Message,
                    row.LevelText,
                    row.Status,
                    duplicateCount,
                    duplicateCount > 1
                        ? FormatText("Navigation_Diagnostics_DuplicateCountFormat", "×{0}", duplicateCount)
                        : string.Empty);
            })
            .ToArray();
    }

    private static string NormalizeIssueMessage(string message)
    {
        var normalized = message;
        if (!normalized.Contains("PlcCode=", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("TaskKey=", StringComparison.OrdinalIgnoreCase))
        {
            var signalIndex = normalized.IndexOf("信号 ", StringComparison.Ordinal);
            if (signalIndex > 0)
            {
                normalized = normalized[signalIndex..];
            }
        }

        return normalized.Replace(" PLC 地址", " 地址", StringComparison.Ordinal);
    }

    private sealed record StartupDiagnosticIssueCandidate(
        string Message,
        EdgeVisualStatus Status,
        string LevelText);

    private bool ShouldIncludeDeviceScopedRow(string? plcCode, string? deviceName)
    {
        var selectedKey = deviceSelectionService.SelectedDeviceKey;
        if (string.Equals(selectedKey, IDeviceSelectionService.AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(plcCode))
        {
            return true;
        }

        var selectedPlcCode = deviceSelectionService.SelectedPlcCode;
        return !string.IsNullOrWhiteSpace(selectedPlcCode)
            ? string.Equals(plcCode?.Trim(), selectedPlcCode, StringComparison.OrdinalIgnoreCase)
            : string.Equals(deviceName?.Trim(), selectedKey, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<MesChannelDiagnosticsRow> BuildMesUploadDiagnostics(
        EdgeSyncDiagnosticsSnapshot syncDiagnostics)
        => syncDiagnostics.Mes.Channels
            .Where(x => ShouldIncludeDeviceScopedRow(x.PlcCode, x.DeviceName))
            .Select(x => new MesChannelDiagnosticsRow(
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, x.ProcessDisplayName),
                FormatPlcIdentity(x.PlcCode, x.DeviceName),
                DiagnosticsTextNormalizer.Normalize(ResolveMesScenario(x)),
                diagnosticsText.FormatMesChannelResult(x.LastResult),
                diagnosticsText.FormatTimestamp(x.LastAttemptAt),
                diagnosticsText.FormatTimestamp(x.LastSuccessAt),
                DiagnosticsTextNormalizer.Normalize(x.LastFailureReason ?? x.LastBlockedReason)))
            .ToArray();

    private static string? ResolveMesScenario(MesChannelDiagnostics diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(diagnostics.Scenario))
        {
            return diagnostics.Scenario;
        }

        return DataPipelineUploadScenarioResolver.Resolve(diagnostics.TaskKey, null, diagnostics.ProcessType);
    }

    private IReadOnlyList<SyncChannelRow> BuildSyncChannels(
        EdgeSyncDiagnosticsSnapshot syncDiagnostics,
        IReadOnlyList<BackgroundServiceRuntimeSnapshot> backgroundServiceRuntime)
    {
        var rows = new List<SyncChannelRow>
        {
            new(
                GetText("Navigation_Diagnostics_ChannelCloud", "云端"),
                BuildCloudStatus(syncDiagnostics.Cloud),
                BuildCloudVisualStatus(syncDiagnostics.Cloud),
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
                BuildMesVisualStatus(syncDiagnostics.Mes),
                FormatText("Navigation_Diagnostics_SyncMesPendingFormat", "重试={0}", syncDiagnostics.Mes.PendingRetryCount),
                syncDiagnostics.Mes.DeadLetters?.TotalCount ?? 0,
                BuildMesLastError(syncDiagnostics.Mes),
                BuildMesNote(syncDiagnostics.Mes))
        };
        rows.AddRange(BuildBackgroundServiceRows(backgroundServiceRuntime));
        return rows;
    }

    private IReadOnlyList<SyncChannelRow> BuildBackgroundServiceRows(
        IReadOnlyList<BackgroundServiceRuntimeSnapshot> snapshots)
        => snapshots
            .Where(static snapshot => IsVisibleBackgroundService(snapshot.ServiceName))
            .OrderBy(static snapshot => GetBackgroundServiceOrder(snapshot.ServiceName))
            .ThenBy(static snapshot => snapshot.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Select(snapshot => new SyncChannelRow(
                BuildBackgroundServiceDisplayName(snapshot.ServiceName),
                BuildBackgroundServiceState(snapshot.State),
                BuildBackgroundServiceVisualStatus(snapshot.State),
                FormatText(
                    "Navigation_Diagnostics_BackgroundChangedFormat",
                    "最近状态：{0:yyyy-MM-dd HH:mm:ss} UTC",
                    snapshot.ChangedAtUtc.ToUniversalTime()),
                0,
                string.IsNullOrWhiteSpace(snapshot.ErrorCode)
                    ? "--"
                    : DiagnosticsTextNormalizer.Normalize(snapshot.ErrorCode),
                BuildBackgroundServiceRepairTarget(snapshot.ServiceName)))
            .ToArray();

    private string BuildBackgroundServiceDisplayName(string serviceName)
    {
        if (IsBackgroundService(serviceName, "ProcessQueueTask"))
        {
            return FormatText(
                "Navigation_Diagnostics_ProcessQueueWorkerFormat",
                "本地主队列（{0}）",
                serviceName);
        }
        if (IsBackgroundService(serviceName, "CloudRetryTask"))
        {
            return FormatText(
                "Navigation_Diagnostics_CloudRetryWorkerFormat",
                "Cloud 重试（{0}）",
                serviceName);
        }
        if (IsBackgroundService(serviceName, "MesRetryTask"))
        {
            return FormatText(
                "Navigation_Diagnostics_MesRetryWorkerFormat",
                "MES 重试（{0}）",
                serviceName);
        }
        return DiagnosticsTextNormalizer.Normalize(serviceName);
    }

    private string BuildBackgroundServiceState(BackgroundServiceRuntimeState state)
        => state switch
        {
            BackgroundServiceRuntimeState.Starting => GetText(
                "Navigation_Diagnostics_BackgroundStarting",
                "启动中"),
            BackgroundServiceRuntimeState.Running => GetText(
                "Navigation_Diagnostics_BackgroundRunning",
                "运行中"),
            BackgroundServiceRuntimeState.Stopping => GetText(
                "Navigation_Diagnostics_BackgroundStopping",
                "停止中"),
            BackgroundServiceRuntimeState.Faulted => GetText(
                "Navigation_Diagnostics_BackgroundFaulted",
                "故障（自动恢复中）"),
            _ => GetText(
                "Navigation_Diagnostics_BackgroundStopped",
                "已停止")
        };

    private static EdgeVisualStatus BuildBackgroundServiceVisualStatus(
        BackgroundServiceRuntimeState state)
        => state switch
        {
            BackgroundServiceRuntimeState.Running => EdgeVisualStatus.Running,
            BackgroundServiceRuntimeState.Faulted => EdgeVisualStatus.Error,
            _ => EdgeVisualStatus.Warning
        };

    private string BuildBackgroundServiceRepairTarget(string serviceName)
    {
        if (IsBackgroundService(serviceName, "ProcessQueueTask"))
        {
            return GetText(
                "Navigation_Diagnostics_ProcessQueueRepair",
                "已启用自动恢复；持续故障时检查本地主队列与持久化存储。");
        }
        if (IsBackgroundService(serviceName, "CloudRetryTask"))
        {
            return GetText(
                "Navigation_Diagnostics_CloudRetryRepair",
                "已启用自动恢复；持续故障时检查 Cloud 本地配置与网络。");
        }
        if (IsBackgroundService(serviceName, "MesRetryTask"))
        {
            return GetText(
                "Navigation_Diagnostics_MesRetryRepair",
                "已启用自动恢复；持续故障时检查 MES 本地配置与网络。");
        }
        return GetText(
            "Navigation_Diagnostics_BackgroundRepair",
            "已启用自动恢复；持续故障时查看诊断日志。");
    }

    private static bool IsVisibleBackgroundService(string serviceName)
        => IsBackgroundService(serviceName, "ProcessQueueTask")
           || IsBackgroundService(serviceName, "CloudRetryTask")
           || IsBackgroundService(serviceName, "MesRetryTask");

    private static int GetBackgroundServiceOrder(string serviceName)
        => IsBackgroundService(serviceName, "ProcessQueueTask")
            ? 0
            : IsBackgroundService(serviceName, "CloudRetryTask")
                ? 1
                : IsBackgroundService(serviceName, "MesRetryTask")
                    ? 2
                    : int.MaxValue;

    private static bool IsBackgroundService(string actual, string expected)
        => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

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

    private static EdgeVisualStatus BuildCloudVisualStatus(CloudSyncDiagnosticsSnapshot cloud)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(cloud) switch
        {
            CloudSyncDiagnosticStatus.Ready => EdgeVisualStatus.Running,
            CloudSyncDiagnosticStatus.WaitingHeartbeat or CloudSyncDiagnosticStatus.WaitingRecovery or CloudSyncDiagnosticStatus.Blocked => EdgeVisualStatus.Warning,
            _ => EdgeVisualStatus.Error
        };

    private string BuildMesStatus(MesSyncDiagnosticsSnapshot mes)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyMes(mes) switch
        {
            MesSyncDiagnosticStatus.PersistenceFaulted => GetText("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            MesSyncDiagnosticStatus.CapacityBlocked => GetText("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            MesSyncDiagnosticStatus.WaitingHeartbeat => GetText("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            _ => diagnosticsText.FormatMesRuntimeState(mes.RuntimeState)
        };

    private static EdgeVisualStatus BuildMesVisualStatus(MesSyncDiagnosticsSnapshot mes)
        => EdgeSyncDiagnosticStatusClassifier.ClassifyMes(mes) switch
        {
            MesSyncDiagnosticStatus.Idle or MesSyncDiagnosticStatus.Retrying => EdgeVisualStatus.Running,
            MesSyncDiagnosticStatus.Backoff or MesSyncDiagnosticStatus.WaitingHeartbeat => EdgeVisualStatus.Warning,
            _ => EdgeVisualStatus.Error
        };

    private string BuildCloudLastError(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (cloud.LastOutcome == CloudCallOutcome.Success &&
            (string.IsNullOrWhiteSpace(cloud.LastReasonCode) ||
             string.Equals(cloud.LastReasonCode, "none", StringComparison.OrdinalIgnoreCase)))
        {
            return "--";
        }

        if (cloud.LastOutcome == CloudCallOutcome.SkippedUploadNotReady)
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

        if (!string.IsNullOrWhiteSpace(cloud.LastBlockedReason))
        {
            return FormatText(
                "Navigation_Diagnostics_SyncBlockedReasonFormat",
                "阻塞：{0}",
                cloud.LastBlockedReason);
        }

        if (cloud.BlockReason != EdgeUploadBlockReason.None)
        {
            return FormatText(
                "Navigation_Diagnostics_SyncBlockedReasonFormat",
                "阻塞：{0}",
                diagnosticsText.FormatBlockReason(cloud.BlockReason));
        }

        if (!string.IsNullOrWhiteSpace(cloud.LastPlcCode)
            || !string.IsNullOrWhiteSpace(cloud.LastDeviceName)
            || !string.IsNullOrWhiteSpace(cloud.LastScenario))
        {
            return FormatText(
                "Navigation_Diagnostics_SyncCloudLastContextFormat",
                "最近：PLC={0}，场景={1}",
                FormatPlcIdentity(cloud.LastPlcCode, cloud.LastDeviceName),
                DiagnosticsTextNormalizer.Normalize(cloud.LastScenario));
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
            .Where(ShouldIncludeDeadLetter)
            .Select(x => DeadLetterRow.From(
                channel,
                x,
                displayNameResolver.ResolveProcessDisplayName(x.ProcessType, null),
                diagnosticsText.FormatTimestamp(x.CreatedAt)))
            .ToArray();

    private bool ShouldIncludeDeadLetter(DeadLetterRecord record)
    {
        var selectedKey = deviceSelectionService.SelectedDeviceKey;
        if (string.Equals(selectedKey, IDeviceSelectionService.AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(record.PlcCode))
        {
            return true;
        }

        var selectedPlcCode = deviceSelectionService.SelectedPlcCode;
        return !string.IsNullOrWhiteSpace(selectedPlcCode)
            ? string.Equals(record.PlcCode.Trim(), selectedPlcCode, StringComparison.OrdinalIgnoreCase)
            : string.Equals(record.DeviceName.Trim(), selectedKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPlcIdentity(string? plcCode, string? deviceName)
    {
        var normalizedCode = DiagnosticsTextNormalizer.Normalize(plcCode);
        var normalizedName = DiagnosticsTextNormalizer.Normalize(deviceName);
        if (normalizedCode == "--")
        {
            return normalizedName;
        }

        return normalizedName == "--"
               || string.Equals(normalizedCode, normalizedName, StringComparison.OrdinalIgnoreCase)
            ? normalizedCode
            : $"{normalizedCode} · {normalizedName}";
    }

    private string GetText(string key, string fallback)
        => languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => languageService.Format(key, fallback, args);
}
