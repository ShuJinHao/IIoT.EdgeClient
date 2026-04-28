using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : NavigationViewModelBase
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IEdgeSyncDiagnosticsQuery _syncDiagnosticsQuery;
    private readonly LocalizedSyncDiagnosticsText _diagnosticsText;
    private readonly DispatcherTimer _refreshTimer;
    private int _refreshInProgress;

    public ObservableCollection<ModuleRegistrationRow> ModuleRegistrations { get; } = [];
    public ObservableCollection<PluginLifecycleRow> PluginStates { get; } = [];
    public ObservableCollection<DeviceModuleBindingRow> DeviceBindings { get; } = [];
    public ObservableCollection<StartupDiagnosticIssueRow> Issues { get; } = [];
    public ObservableCollection<MesChannelDiagnosticsRow> MesUploadDiagnostics { get; } = [];

    private string _discoveredModulesSummary = "正在检查已发现模块...";
    public string DiscoveredModulesSummary
    {
        get => _discoveredModulesSummary;
        private set
        {
            _discoveredModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _enabledModulesSummary = "正在检查已启用模块...";
    public string EnabledModulesSummary
    {
        get => _enabledModulesSummary;
        private set
        {
            _enabledModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _activatedModulesSummary = "正在检查已激活模块...";
    public string ActivatedModulesSummary
    {
        get => _activatedModulesSummary;
        private set
        {
            _activatedModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _configurationProfileSummary = "配置概况：--";
    public string ConfigurationProfileSummary
    {
        get => _configurationProfileSummary;
        private set
        {
            _configurationProfileSummary = value;
            OnPropertyChanged();
        }
    }

    private string _lastUpdatedSummary = "启动诊断尚未生成。";
    public string LastUpdatedSummary
    {
        get => _lastUpdatedSummary;
        private set
        {
            _lastUpdatedSummary = value;
            OnPropertyChanged();
        }
    }

    private string _deviceSummary = "设备：未知";
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set
        {
            _deviceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudGateSummary = "上传门禁：--";
    public string CloudGateSummary
    {
        get => _cloudGateSummary;
        private set
        {
            _cloudGateSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudRuntimeSummary = "云端运行：空闲";
    public string CloudRuntimeSummary
    {
        get => _cloudRuntimeSummary;
        private set
        {
            _cloudRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudResultSummary = "最近结果：--";
    public string CloudResultSummary
    {
        get => _cloudResultSummary;
        private set
        {
            _cloudResultSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPendingSummary = "待处理：重试=0，日志=0，产能=0";
    public string CloudPendingSummary
    {
        get => _cloudPendingSummary;
        private set
        {
            _cloudPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudCapacitySummary = "产能阻塞：否";
    public string CloudCapacitySummary
    {
        get => _cloudCapacitySummary;
        private set
        {
            _cloudCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPersistenceSummary = "存储故障：否";
    public string CloudPersistenceSummary
    {
        get => _cloudPersistenceSummary;
        private set
        {
            _cloudPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastAttemptSummary = "最近尝试：--";
    public string CloudLastAttemptSummary
    {
        get => _cloudLastAttemptSummary;
        private set
        {
            _cloudLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastSuccessSummary = "最近成功：--";
    public string CloudLastSuccessSummary
    {
        get => _cloudLastSuccessSummary;
        private set
        {
            _cloudLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastFailureSummary = "最近失败：--";
    public string CloudLastFailureSummary
    {
        get => _cloudLastFailureSummary;
        private set
        {
            _cloudLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesRuntimeSummary = "MES运行：空闲";
    public string MesRuntimeSummary
    {
        get => _mesRuntimeSummary;
        private set
        {
            _mesRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPendingSummary = "待处理：重试=0";
    public string MesPendingSummary
    {
        get => _mesPendingSummary;
        private set
        {
            _mesPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesCapacitySummary = "产能阻塞：否";
    public string MesCapacitySummary
    {
        get => _mesCapacitySummary;
        private set
        {
            _mesCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPersistenceSummary = "存储故障：否";
    public string MesPersistenceSummary
    {
        get => _mesPersistenceSummary;
        private set
        {
            _mesPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastAttemptSummary = "最近尝试：--";
    public string MesLastAttemptSummary
    {
        get => _mesLastAttemptSummary;
        private set
        {
            _mesLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastSuccessSummary = "最近成功：--";
    public string MesLastSuccessSummary
    {
        get => _mesLastSuccessSummary;
        private set
        {
            _mesLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastFailureSummary = "最近失败：--";
    public string MesLastFailureSummary
    {
        get => _mesLastFailureSummary;
        private set
        {
            _mesLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _contextPersistenceSummary = "损坏文件数：0";
    public string ContextPersistenceSummary
    {
        get => _contextPersistenceSummary;
        private set
        {
            _contextPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    public DiagnosticsViewModel(
        IStartupDiagnosticsStore diagnosticsStore,
        IEdgeSyncDiagnosticsQuery syncDiagnosticsQuery,
        IAppLanguageService languageService)
        : base(languageService, CoreViewIds.Diagnostics, "Navigation_Menu_CoreDiagnostics", "系统诊断")
    {
        _diagnosticsStore = diagnosticsStore;
        _syncDiagnosticsQuery = syncDiagnosticsQuery;
        _diagnosticsText = new LocalizedSyncDiagnosticsText(languageService);
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        _ = SafeRefreshAsync();
    }

    public override Task OnActivatedAsync() => RefreshAsync();

    internal Task RefreshAsync(CancellationToken ct = default)
        => RefreshIfIdleAsync(ct);

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await SafeRefreshAsync();
    }

    private async Task SafeRefreshAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshIfIdleAsync(ct);
        }
        catch
        {
            // 诊断页刷新失败不应导致界面轮询崩溃。
        }
    }

    private async Task RefreshIfIdleAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await RefreshCoreAsync(ct);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private async Task RefreshCoreAsync(CancellationToken ct)
    {
        var report = _diagnosticsStore.Current;
        var syncDiagnostics = await _syncDiagnosticsQuery.GetCurrentAsync(ct);
        var moduleNameMap = BuildModuleNameMap(report);

        DiscoveredModulesSummary = BuildModuleSummary(
            "已发现模块",
            "当前未发现模块。",
            report.DiscoveredModules,
            moduleNameMap);

        EnabledModulesSummary = BuildModuleSummary(
            "已启用模块",
            "当前没有配置为启用的模块。",
            report.EnabledModules,
            moduleNameMap);

        ActivatedModulesSummary = BuildModuleSummary(
            "已激活模块",
            "当前没有已激活模块。",
            report.ActivatedModules,
            moduleNameMap);

        ConfigurationProfileSummary = BuildConfigurationProfileSummary(report.ConfigurationProfile);
        LastUpdatedSummary = report.GeneratedAt == DateTime.MinValue
            ? "启动诊断尚未生成。"
            : $"最近生成：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";

        DeviceSummary = $"设备：{syncDiagnostics.DeviceName}";

        var cloudGate = syncDiagnostics.Cloud.GateState switch
        {
            EdgeUploadGateState.Ready => "已就绪",
            _ when syncDiagnostics.Cloud.IsPausedWaitingForRecovery => "等待恢复",
            _ => FormatText("Navigation_Sync_StatusBlockedFormat", "已阻塞（{0}）", _diagnosticsText.FormatBlockReason(syncDiagnostics.Cloud.BlockReason))
        };

        CloudGateSummary = $"上传门禁：{cloudGate}";
        CloudRuntimeSummary = $"云端运行：{_diagnosticsText.FormatCloudRuntimeState(syncDiagnostics.Cloud.RuntimeState)}";
        CloudResultSummary =
            $"最近结果：{_diagnosticsText.FormatCloudOutcome(syncDiagnostics.Cloud.LastOutcome, syncDiagnostics.Cloud.LastReasonCode, syncDiagnostics.Cloud.LastProcessType)}";
        CloudPendingSummary =
            $"待处理：重试={syncDiagnostics.Cloud.PendingRetryCount}，日志={syncDiagnostics.Cloud.PendingDeviceLogCount}，产能={syncDiagnostics.Cloud.PendingCapacityCount}";
        CloudCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(
            syncDiagnostics.Cloud.IsCapacityBlocked,
            syncDiagnostics.Cloud.BlockedChannel,
            syncDiagnostics.Cloud.BlockedReason,
            syncDiagnostics.Cloud.LastCapacityBlockAt);
        CloudPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(
            syncDiagnostics.Cloud.IsPersistenceFaulted,
            syncDiagnostics.Cloud.LastPersistenceFaultAt,
            syncDiagnostics.Cloud.PersistenceFaultMessage);
        CloudLastAttemptSummary = $"最近尝试：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastAttemptAt)}";
        CloudLastSuccessSummary = $"最近成功：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastSuccessAt)}";
        CloudLastFailureSummary = $"最近失败：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastFailureAt)}";

        MesRuntimeSummary = $"MES运行：{_diagnosticsText.FormatMesRuntimeState(syncDiagnostics.Mes.RuntimeState)}";
        MesPendingSummary = $"待处理：重试={syncDiagnostics.Mes.PendingRetryCount}";
        MesCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(
            syncDiagnostics.Mes.IsCapacityBlocked,
            syncDiagnostics.Mes.BlockedChannel,
            syncDiagnostics.Mes.BlockedReason,
            syncDiagnostics.Mes.LastCapacityBlockAt);
        MesPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(
            syncDiagnostics.Mes.IsPersistenceFaulted,
            syncDiagnostics.Mes.LastPersistenceFaultAt,
            syncDiagnostics.Mes.PersistenceFaultMessage);
        MesLastAttemptSummary = $"最近尝试：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastAttemptAt)}";
        MesLastSuccessSummary = $"最近成功：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastSuccessAt)}";
        MesLastFailureSummary =
            $"最近失败：{LocalizedSyncDiagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastFailureAt)}（{NormalizeText(syncDiagnostics.Mes.LastFailureReason)}）";

        ContextPersistenceSummary = _diagnosticsText.FormatContextPersistenceSummary(syncDiagnostics.ContextPersistence);

        ReplaceItems(
            ModuleRegistrations,
            report.ModuleRegistrations.Select(x => new ModuleRegistrationRow(
                x.ModuleId,
                _diagnosticsText.FormatProcessType(x.ProcessType),
                x.AssemblyName,
                x.IsEnabled,
                x.HasCellDataRegistration,
                x.HasRuntimeFactory,
                x.HasCloudUploader,
                x.HasMesUploader,
                x.HasHardwareProfile)));

        ReplaceItems(
            PluginStates,
            report.PluginStates.Select(x => new PluginLifecycleRow(
                x.ModuleId,
                _diagnosticsText.FormatProcessType(x.ProcessType),
                _diagnosticsText.FormatProcessType(x.ProcessType),
                x.Version,
                _diagnosticsText.FormatPluginLifecycleState(x.State),
                NormalizeText(x.Message))));

        ReplaceItems(
            DeviceBindings,
            report.DeviceBindings.Select(x => new DeviceModuleBindingRow(
                x.DeviceName,
                NormalizeText(x.ModuleId),
                x.ModuleExists,
                x.ModuleEnabled,
                x.HasIoMappings)));

        ReplaceItems(
            Issues,
            report.Issues.Select(x => new StartupDiagnosticIssueRow(
                x.Code,
                NormalizeText(x.ModuleId),
                NormalizeText(x.DeviceName),
                NormalizeText(x.Message))));

        ReplaceItems(
            MesUploadDiagnostics,
            syncDiagnostics.Mes.Channels.Select(x => new MesChannelDiagnosticsRow(
                _diagnosticsText.FormatProcessType(x.ProcessType),
                _diagnosticsText.FormatMesChannelResult(x.LastResult),
                LocalizedSyncDiagnosticsText.FormatTimestamp(x.LastAttemptAt),
                LocalizedSyncDiagnosticsText.FormatTimestamp(x.LastSuccessAt),
                NormalizeText(x.LastFailureReason))));

        SetStatus(report.Issues.Count == 0
            ? "启动诊断正常。"
            : $"启动诊断发现 {report.Issues.Count} 个问题。");
    }

    private IReadOnlyDictionary<string, string> BuildModuleNameMap(StartupDiagnosticsReport report)
    {
        var pairs = report.PluginStates
            .Select(x => new KeyValuePair<string, string>(
                x.ModuleId,
                !string.IsNullOrWhiteSpace(x.DisplayName)
                    ? _diagnosticsText.FormatProcessType(x.ProcessType)
                    : _diagnosticsText.FormatProcessType(x.ProcessType)))
            .Concat(report.ModuleRegistrations.Select(x => new KeyValuePair<string, string>(
                x.ModuleId,
                _diagnosticsText.FormatProcessType(x.ProcessType))));

        return pairs
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.Value).FirstOrDefault(y => !string.IsNullOrWhiteSpace(y)) ?? x.Key,
                StringComparer.OrdinalIgnoreCase);
    }

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
            .Select(x => ResolveModuleDisplayName(x, moduleNameMap))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"{label}：{string.Join("、", names)}";
    }

    private string ResolveModuleDisplayName(string moduleId, IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (moduleNameMap.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return _diagnosticsText.FormatProcessType(moduleId);
    }

    private static string BuildConfigurationProfileSummary(ConfigurationProfileSnapshot profile)
    {
        if (string.IsNullOrWhiteSpace(profile.MachineProfile))
        {
            return $"环境：{profile.EnvironmentName}；机型：未配置；运行目录：{profile.RuntimeDataRoot}";
        }

        var loadState = profile.IsMachineProfileLoaded
            ? $"已从 {profile.MachineProfileFileName} 加载"
            : $"未找到 {profile.MachineProfileFileName}";
        return $"环境：{profile.EnvironmentName}；机型：{profile.MachineProfile}（{loadState}）；运行目录：{profile.RuntimeDataRoot}";
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;

    public sealed record PluginLifecycleRow(
        string ModuleId,
        string DisplayName,
        string ProcessType,
        string Version,
        string State,
        string Message);

    public sealed record ModuleRegistrationRow(
        string ModuleId,
        string ProcessType,
        string AssemblyName,
        bool IsEnabled,
        bool HasCellDataRegistration,
        bool HasRuntimeFactory,
        bool HasCloudUploader,
        bool HasMesUploader,
        bool HasHardwareProfile);

    public sealed record DeviceModuleBindingRow(
        string DeviceName,
        string ModuleId,
        bool ModuleExists,
        bool ModuleEnabled,
        bool HasIoMappings);

    public sealed record StartupDiagnosticIssueRow(
        string Code,
        string ModuleId,
        string DeviceName,
        string Message);

    public sealed record MesChannelDiagnosticsRow(
        string ProcessType,
        string LastResult,
        string LastAttemptAt,
        string LastSuccessAt,
        string LastFailureReason);
}
