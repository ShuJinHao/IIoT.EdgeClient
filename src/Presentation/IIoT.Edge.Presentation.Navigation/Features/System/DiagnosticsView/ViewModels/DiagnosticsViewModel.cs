using IIoT.Edge.Application.Modules.Diagnostics;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed class DiagnosticsViewModel : NavigationViewModelBase
{
    private readonly IStartupDiagnosticsStore _diagnosticsStore;
    private readonly IEdgeSyncDiagnosticsQuery _syncDiagnosticsQuery;
    private readonly IDeadLetterMaintenanceService? _deadLetterMaintenanceService;
    private readonly LocalizedSyncDiagnosticsText _diagnosticsText;
    private readonly DispatcherTimer _refreshTimer;
    private int _refreshInProgress;

    public ObservableCollection<ModuleRegistrationRow> ModuleRegistrations { get; } = [];
    public ObservableCollection<PluginLifecycleRow> PluginStates { get; } = [];
    public ObservableCollection<DeviceModuleBindingRow> DeviceBindings { get; } = [];
    public ObservableCollection<StartupDiagnosticIssueRow> Issues { get; } = [];
    public ObservableCollection<MesChannelDiagnosticsRow> MesUploadDiagnostics { get; } = [];
    public ObservableCollection<DeadLetterRow> CloudDeadLetters { get; } = [];
    public ObservableCollection<DeadLetterRow> MesDeadLetters { get; } = [];

    public ICommand RequeueDeadLetterCommand { get; }
    public ICommand DeleteDeadLetterCommand { get; }

    private string _discoveredModulesSummary = string.Empty;
    public string DiscoveredModulesSummary
    {
        get => _discoveredModulesSummary;
        private set
        {
            _discoveredModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _enabledModulesSummary = string.Empty;
    public string EnabledModulesSummary
    {
        get => _enabledModulesSummary;
        private set
        {
            _enabledModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _activatedModulesSummary = string.Empty;
    public string ActivatedModulesSummary
    {
        get => _activatedModulesSummary;
        private set
        {
            _activatedModulesSummary = value;
            OnPropertyChanged();
        }
    }

    private string _configurationProfileSummary = string.Empty;
    public string ConfigurationProfileSummary
    {
        get => _configurationProfileSummary;
        private set
        {
            _configurationProfileSummary = value;
            OnPropertyChanged();
        }
    }

    private string _lastUpdatedSummary = string.Empty;
    public string LastUpdatedSummary
    {
        get => _lastUpdatedSummary;
        private set
        {
            _lastUpdatedSummary = value;
            OnPropertyChanged();
        }
    }

    private string _deviceSummary = string.Empty;
    public string DeviceSummary
    {
        get => _deviceSummary;
        private set
        {
            _deviceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudGateSummary = string.Empty;
    public string CloudGateSummary
    {
        get => _cloudGateSummary;
        private set
        {
            _cloudGateSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudRuntimeSummary = string.Empty;
    public string CloudRuntimeSummary
    {
        get => _cloudRuntimeSummary;
        private set
        {
            _cloudRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudResultSummary = string.Empty;
    public string CloudResultSummary
    {
        get => _cloudResultSummary;
        private set
        {
            _cloudResultSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPendingSummary = string.Empty;
    public string CloudPendingSummary
    {
        get => _cloudPendingSummary;
        private set
        {
            _cloudPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudCapacitySummary = string.Empty;
    public string CloudCapacitySummary
    {
        get => _cloudCapacitySummary;
        private set
        {
            _cloudCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudPersistenceSummary = string.Empty;
    public string CloudPersistenceSummary
    {
        get => _cloudPersistenceSummary;
        private set
        {
            _cloudPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastAttemptSummary = string.Empty;
    public string CloudLastAttemptSummary
    {
        get => _cloudLastAttemptSummary;
        private set
        {
            _cloudLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastSuccessSummary = string.Empty;
    public string CloudLastSuccessSummary
    {
        get => _cloudLastSuccessSummary;
        private set
        {
            _cloudLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudLastFailureSummary = string.Empty;
    public string CloudLastFailureSummary
    {
        get => _cloudLastFailureSummary;
        private set
        {
            _cloudLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesRuntimeSummary = string.Empty;
    public string MesRuntimeSummary
    {
        get => _mesRuntimeSummary;
        private set
        {
            _mesRuntimeSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPendingSummary = string.Empty;
    public string MesPendingSummary
    {
        get => _mesPendingSummary;
        private set
        {
            _mesPendingSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesCapacitySummary = string.Empty;
    public string MesCapacitySummary
    {
        get => _mesCapacitySummary;
        private set
        {
            _mesCapacitySummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesPersistenceSummary = string.Empty;
    public string MesPersistenceSummary
    {
        get => _mesPersistenceSummary;
        private set
        {
            _mesPersistenceSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastAttemptSummary = string.Empty;
    public string MesLastAttemptSummary
    {
        get => _mesLastAttemptSummary;
        private set
        {
            _mesLastAttemptSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastSuccessSummary = string.Empty;
    public string MesLastSuccessSummary
    {
        get => _mesLastSuccessSummary;
        private set
        {
            _mesLastSuccessSummary = value;
            OnPropertyChanged();
        }
    }

    private string _mesLastFailureSummary = string.Empty;
    public string MesLastFailureSummary
    {
        get => _mesLastFailureSummary;
        private set
        {
            _mesLastFailureSummary = value;
            OnPropertyChanged();
        }
    }

    private string _contextPersistenceSummary = string.Empty;
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
        IAppLanguageService languageService,
        IDeadLetterMaintenanceService? deadLetterMaintenanceService = null,
        IProductionTimeProvider? productionTime = null)
        : base(languageService, CoreViewIds.Diagnostics, "Navigation_Menu_CoreDiagnostics", "系统诊断")
    {
        _diagnosticsStore = diagnosticsStore;
        _syncDiagnosticsQuery = syncDiagnosticsQuery;
        _deadLetterMaintenanceService = deadLetterMaintenanceService;
        _diagnosticsText = new LocalizedSyncDiagnosticsText(languageService, productionTime);
        RequeueDeadLetterCommand = new AsyncCommand<DeadLetterRow>(RequeueDeadLetterAsync, CanOperateDeadLetter);
        DeleteDeadLetterCommand = new AsyncCommand<DeadLetterRow>(DeleteDeadLetterAsync, CanOperateDeadLetter);
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        ApplyInitialSummaries();
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
            GetText("Navigation_Diagnostics_DiscoveredLabel", "已发现模块"),
            GetText("Navigation_Diagnostics_NoDiscoveredModules", "当前未发现模块。"),
            report.DiscoveredModules,
            moduleNameMap);

        EnabledModulesSummary = BuildModuleSummary(
            GetText("Navigation_Diagnostics_EnabledLabel", "已启用模块"),
            GetText("Navigation_Diagnostics_NoEnabledModules", "当前没有配置为启用的模块。"),
            report.EnabledModules,
            moduleNameMap);

        ActivatedModulesSummary = BuildModuleSummary(
            GetText("Navigation_Diagnostics_ActivatedLabel", "已激活模块"),
            GetText("Navigation_Diagnostics_NoActivatedModules", "当前没有已激活模块。"),
            report.ActivatedModules,
            moduleNameMap);

        ConfigurationProfileSummary = BuildConfigurationProfileSummary(report.ConfigurationProfile);
        LastUpdatedSummary = report.GeneratedAt == DateTime.MinValue
            ? GetText("Navigation_Diagnostics_StartupPending", "启动诊断尚未生成。")
            : FormatText("Navigation_Diagnostics_LastGeneratedFormat", "最近生成：{0:yyyy-MM-dd HH:mm:ss}", report.GeneratedAt);

        DeviceSummary = FormatText("Navigation_Diagnostics_DeviceFormat", "设备：{0}", syncDiagnostics.DeviceName);

        var cloudGate = EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(syncDiagnostics.Cloud) switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => GetText("Navigation_Sync_StatusPersistenceFaulted", "存储故障"),
            CloudSyncDiagnosticStatus.CapacityBlocked => GetText("Navigation_Sync_StatusCapacityBlocked", "产能阻塞"),
            CloudSyncDiagnosticStatus.WaitingHeartbeat => GetText("Navigation_Sync_StatusWaitingHeartbeat", "等待心跳恢复"),
            CloudSyncDiagnosticStatus.Ready => GetText("Navigation_Sync_StatusReady", "已就绪"),
            CloudSyncDiagnosticStatus.WaitingRecovery => GetText("Navigation_Sync_StatusWaitingRecovery", "等待恢复"),
            _ => FormatText("Navigation_Sync_StatusBlockedFormat", "已阻塞（{0}）", _diagnosticsText.FormatBlockReason(syncDiagnostics.Cloud.BlockReason))
        };

        CloudGateSummary = FormatText("Navigation_Sync_UploadGateFormat", "上传门禁：{0}", cloudGate);
        CloudRuntimeSummary = FormatText(
            "Navigation_Diagnostics_CloudRuntimeFormat",
            "云端运行：{0}",
            _diagnosticsText.FormatCloudRuntimeState(syncDiagnostics.Cloud.RuntimeState));
        CloudResultSummary = FormatText(
            "Navigation_Sync_LastResultFormat",
            "最近结果：{0}",
            _diagnosticsText.FormatCloudOutcome(
                syncDiagnostics.Cloud.LastOutcome,
                syncDiagnostics.Cloud.LastReasonCode,
                syncDiagnostics.Cloud.LastProcessType,
                syncDiagnostics.Cloud.LastProcessDisplayName));
        CloudPendingSummary = FormatText(
            "Navigation_Sync_PendingCloudFormat",
            "待处理：重试={0}，日志={1}，产能={2}",
            syncDiagnostics.Cloud.PendingRetryCount,
            syncDiagnostics.Cloud.PendingDeviceLogCount,
            syncDiagnostics.Cloud.PendingCapacityCount)
            + FormatText(
                "Navigation_Sync_DeadLetterSuffixFormat",
                "，死信={0}",
                syncDiagnostics.Cloud.DeadLetters?.TotalCount ?? 0);
        CloudCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(
            syncDiagnostics.Cloud.IsCapacityBlocked,
            syncDiagnostics.Cloud.BlockedChannel,
            syncDiagnostics.Cloud.BlockedReason,
            syncDiagnostics.Cloud.LastCapacityBlockAt);
        CloudPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(
            syncDiagnostics.Cloud.IsPersistenceFaulted,
            syncDiagnostics.Cloud.LastPersistenceFaultAt,
            syncDiagnostics.Cloud.PersistenceFaultMessage);
        CloudLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", _diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastAttemptAt));
        CloudLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", _diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastSuccessAt));
        CloudLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", _diagnosticsText.FormatTimestamp(syncDiagnostics.Cloud.LastFailureAt));

        MesRuntimeSummary = FormatText(
            "Navigation_Diagnostics_MesRuntimeFormat",
            "MES运行：{0}",
            _diagnosticsText.FormatMesRuntimeState(syncDiagnostics.Mes.RuntimeState));
        MesPendingSummary = FormatText("Navigation_Sync_PendingMesFormat", "待处理：重试={0}", syncDiagnostics.Mes.PendingRetryCount)
            + FormatText(
                "Navigation_Sync_DeadLetterSuffixFormat",
                "，死信={0}",
                syncDiagnostics.Mes.DeadLetters?.TotalCount ?? 0);
        MesCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(
            syncDiagnostics.Mes.IsCapacityBlocked,
            syncDiagnostics.Mes.BlockedChannel,
            syncDiagnostics.Mes.BlockedReason,
            syncDiagnostics.Mes.LastCapacityBlockAt);
        MesPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(
            syncDiagnostics.Mes.IsPersistenceFaulted,
            syncDiagnostics.Mes.LastPersistenceFaultAt,
            syncDiagnostics.Mes.PersistenceFaultMessage);
        MesLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", _diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastAttemptAt));
        MesLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", _diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastSuccessAt));
        MesLastFailureSummary = FormatText(
            "Navigation_Sync_LastFailureWithReasonFormat",
            "最近失败：{0}（{1}）",
            _diagnosticsText.FormatTimestamp(syncDiagnostics.Mes.LastFailureAt),
            NormalizeText(syncDiagnostics.Mes.LastFailureReason));

        ContextPersistenceSummary = _diagnosticsText.FormatContextPersistenceSummary(syncDiagnostics.ContextPersistence);

        ReplaceItems(
            ModuleRegistrations,
            report.ModuleRegistrations.Select(x => new ModuleRegistrationRow(
                x.ModuleId,
                ResolveProcessDisplayName(x.ModuleId, x.ProcessType, moduleNameMap),
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
                ResolveProcessDisplayName(x.ProcessType, x.DisplayName),
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
                ResolveProcessDisplayName(x.ProcessType, x.ProcessDisplayName),
                _diagnosticsText.FormatMesChannelResult(x.LastResult),
                _diagnosticsText.FormatTimestamp(x.LastAttemptAt),
                _diagnosticsText.FormatTimestamp(x.LastSuccessAt),
                NormalizeText(x.LastFailureReason))));

        ReplaceItems(
            CloudDeadLetters,
            (syncDiagnostics.Cloud.DeadLetters?.LatestRecords ?? [])
                .Select(x => DeadLetterRow.From(
                    DataPipelineRetryChannel.Cloud,
                    x,
                    ResolveProcessDisplayName(x.ProcessType, null),
                    _diagnosticsText.FormatTimestamp(x.CreatedAt))));

        ReplaceItems(
            MesDeadLetters,
            (syncDiagnostics.Mes.DeadLetters?.LatestRecords ?? [])
                .Select(x => DeadLetterRow.From(
                    DataPipelineRetryChannel.Mes,
                    x,
                    ResolveProcessDisplayName(x.ProcessType, null),
                    _diagnosticsText.FormatTimestamp(x.CreatedAt))));

        SetStatus(report.Issues.Count == 0
            ? GetText("Navigation_Diagnostics_StartupOk", "启动诊断正常。")
            : FormatText("Navigation_Diagnostics_StartupIssueCountFormat", "启动诊断发现 {0} 个问题。", report.Issues.Count));
    }

    private IReadOnlyDictionary<string, string> BuildModuleNameMap(StartupDiagnosticsReport report)
    {
        var pairs = report.PluginStates
            .Select(x => new KeyValuePair<string, string>(
                x.ModuleId,
                ResolveProcessDisplayName(x.ProcessType, x.DisplayName)))
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
        return FormatText("Navigation_Diagnostics_ModuleSummaryFormat", "{0}：{1}", label, string.Join(GetText("Navigation_ListSeparator", "、"), names));
    }

    private string ResolveModuleDisplayName(string moduleId, IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (moduleNameMap.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return _diagnosticsText.FormatProcessType(moduleId);
    }

    private string ResolveProcessDisplayName(
        string moduleId,
        string? processType,
        IReadOnlyDictionary<string, string> moduleNameMap)
    {
        if (moduleNameMap.TryGetValue(moduleId, out var displayName) && !string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return _diagnosticsText.FormatProcessType(processType);
    }

    private string ResolveProcessDisplayName(string? processType, string? processDisplayName)
        => string.IsNullOrWhiteSpace(processDisplayName)
            ? _diagnosticsText.FormatProcessType(processType)
            : processDisplayName;

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

        var profileFileName = NormalizeText(profile.MachineProfileFileName);
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

    private void ApplyInitialSummaries()
    {
        DiscoveredModulesSummary = GetText("Navigation_Diagnostics_DiscoveredChecking", "正在检查已发现模块...");
        EnabledModulesSummary = GetText("Navigation_Diagnostics_EnabledChecking", "正在检查已启用模块...");
        ActivatedModulesSummary = GetText("Navigation_Diagnostics_ActivatedChecking", "正在检查已激活模块...");
        ConfigurationProfileSummary = GetText("Navigation_Diagnostics_ConfigPlaceholder", "配置概况：--");
        LastUpdatedSummary = GetText("Navigation_Diagnostics_StartupPending", "启动诊断尚未生成。");
        DeviceSummary = FormatText("Navigation_Diagnostics_DeviceFormat", "设备：{0}", GetText("Navigation_Unknown", "未知"));
        CloudGateSummary = FormatText("Navigation_Sync_UploadGateFormat", "上传门禁：{0}", "--");
        CloudRuntimeSummary = FormatText("Navigation_Diagnostics_CloudRuntimeFormat", "云端运行：{0}", GetText("Navigation_Runtime_Idle", "空闲"));
        CloudResultSummary = FormatText("Navigation_Sync_LastResultFormat", "最近结果：{0}", "--");
        CloudPendingSummary = FormatText("Navigation_Sync_PendingCloudFormat", "待处理：重试={0}，日志={1}，产能={2}", 0, 0, 0);
        CloudCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(false, null, string.Empty, null);
        CloudPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(false, null, null);
        CloudLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", "--");
        CloudLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", "--");
        CloudLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", "--");
        MesRuntimeSummary = FormatText("Navigation_Diagnostics_MesRuntimeFormat", "MES运行：{0}", GetText("Navigation_Runtime_Idle", "空闲"));
        MesPendingSummary = FormatText("Navigation_Sync_PendingMesFormat", "待处理：重试={0}", 0);
        MesCapacitySummary = _diagnosticsText.FormatCapacityBlockedSummary(false, null, string.Empty, null);
        MesPersistenceSummary = _diagnosticsText.FormatPersistenceFaultSummary(false, null, null);
        MesLastAttemptSummary = FormatText("Navigation_Sync_LastAttemptFormat", "最近尝试：{0}", "--");
        MesLastSuccessSummary = FormatText("Navigation_Sync_LastSuccessFormat", "最近成功：{0}", "--");
        MesLastFailureSummary = FormatText("Navigation_Sync_LastFailureFormat", "最近失败：{0}", "--");
        ContextPersistenceSummary = _diagnosticsText.FormatContextPersistenceSummary(new(0, null));
    }

    private static string NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "--"
            : value;

    private bool CanOperateDeadLetter(DeadLetterRow? row)
        => _deadLetterMaintenanceService is not null && row is not null;

    private async Task RequeueDeadLetterAsync(DeadLetterRow row)
    {
        if (_deadLetterMaintenanceService is null)
        {
            SetError("死信运维服务未注册。");
            return;
        }

        var result = await _deadLetterMaintenanceService.RequeueAsync(row.Channel, row.Id);
        if (result.IsSuccess)
        {
            SetStatus(result.Message);
            await RefreshAsync();
            return;
        }

        SetError(result.Message);
    }

    private async Task DeleteDeadLetterAsync(DeadLetterRow row)
    {
        if (_deadLetterMaintenanceService is null)
        {
            SetError("死信运维服务未注册。");
            return;
        }

        var result = await _deadLetterMaintenanceService.DeleteAsync(row.Channel, row.Id);
        if (result.IsSuccess)
        {
            SetStatus(result.Message);
            await RefreshAsync();
            return;
        }

        SetError(result.Message);
    }

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

    public sealed record DeadLetterRow(
        DataPipelineRetryChannel Channel,
        long Id,
        string ProcessType,
        string FailedTarget,
        string FailureStage,
        string Source,
        string CreatedAt,
        string FailureReason,
        string CellDataJson)
    {
        public static DeadLetterRow From(
            DataPipelineRetryChannel channel,
            DeadLetterRecord record,
            string processDisplayName,
            string createdAt)
            => new(
                channel,
                record.Id,
                processDisplayName,
                record.FailedTarget,
                record.FailureStage,
                $"{record.SourceTable}/{record.SourceRecordId?.ToString() ?? "--"}",
                createdAt,
                NormalizeText(record.FailureReason),
                NormalizeText(record.CellDataJson));
    }
}
