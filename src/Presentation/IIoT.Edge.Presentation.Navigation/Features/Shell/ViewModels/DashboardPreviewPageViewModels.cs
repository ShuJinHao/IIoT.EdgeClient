using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

internal sealed class DashboardPreviewRuntimeViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private const string EmptyValue = "—";
    private const int AlertLimit = 50;
    private const int UploadHealthSegmentLimit = 6;
    private const int LatencyWarningThresholdMs = 5000;
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly DashboardViewModel _source;
    private readonly IAppLanguageService _languageService;
    private readonly ISystemLogDisplayStore _logDisplayStore;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly DispatcherTimer _diagnosticsTimer;

    private DateTime? _lastCloudSuccessAt;
    private DateTime? _lastCloudFailureAt;
    private DateTime? _lastMesSuccessAt;
    private DateTime? _lastMesFailureAt;
    private DateTime? _latestUploadSuccessAt;
    private DateTime? _latestUploadFailureAt;
    private bool _cloudUploadEnabled;
    private bool _mesUploadEnabled;
    private bool _isLogStoreSubscribed;
    private int _diagnosticsRefreshInFlight;
    private int _deadLetterUploadCount;
    private string _cloudStateText = EmptyValue;
    private string _cloudProbeText = string.Empty;
    private string _mesStateText = EmptyValue;
    private string _mesProbeText = string.Empty;
    private string _plcLatencyText = EmptyValue;
    private EdgeVisualStatus _cloudStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _mesStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _plcLatencyStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _deviceLinksStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _uploadHealthStatus = EdgeVisualStatus.Offline;

    public DashboardPreviewRuntimeViewModel(
        DashboardViewModel source,
        IAppLanguageService languageService,
        ISystemLogDisplayStore logDisplayStore,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IPlcConnectionManager plcConnectionManager)
    {
        _source = source;
        _languageService = languageService;
        _logDisplayStore = logDisplayStore;
        _runtimeConfig = runtimeConfig;
        _diagnosticsQuery = diagnosticsQuery;
        _plcConnectionManager = plcConnectionManager;
        _diagnosticsTimer = new DispatcherTimer { Interval = DiagnosticsRefreshInterval };
        _diagnosticsTimer.Tick += OnDiagnosticsTimerTick;

        var runtimeSnapshot = _runtimeConfig.Current;
        _cloudUploadEnabled = runtimeSnapshot.CloudUploadEnabled;
        _mesUploadEnabled = runtimeSnapshot.MesUploadEnabled;

        _source.PropertyChanged += OnSourcePropertyChanged;
        _languageService.LanguageChanged += OnLanguageChanged;
    }

    public ObservableCollection<DashboardPreviewAlertItem> AlertItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadChannelItem> UploadChannelItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadHealthSegment> UploadHealthSegments { get; } = [];

    public string RecentHourOutput => Normalize(_source.RecentHourOutput);

    public string RecentHourDescription => FormatText(
        "Navigation_DashboardPreview_RecentHourWindowFormat",
        "窗口：{0}",
        Normalize(_source.RecentHourLabel));

    public string ConnectedDevices => Normalize(_source.ConnectedDevices);

    public string CurrentBatch => Normalize(_source.CurrentBatch);

    public string CloudStateText => _cloudStateText;

    public string CloudProbeText => _cloudProbeText;

    public bool HasCloudProbeText => !string.IsNullOrWhiteSpace(_cloudProbeText);

    public string MesStateText => _mesStateText;

    public string MesProbeText => _mesProbeText;

    public bool HasMesProbeText => !string.IsNullOrWhiteSpace(_mesProbeText);

    public string CloudLatencyText => _cloudStateText;

    public string MesLatencyText => _mesStateText;

    public string PlcLatencyText => _plcLatencyText;

    public EdgeVisualStatus CloudLatencyStatus => _cloudStatus;

    public EdgeVisualStatus MesLatencyStatus => _mesStatus;

    public EdgeVisualStatus CloudStatus => _cloudStatus;

    public EdgeVisualStatus MesStatus => _mesStatus;

    public EdgeVisualStatus PlcLatencyStatus => _plcLatencyStatus;

    public EdgeVisualStatus DeviceLinksStatus => _deviceLinksStatus;

    public string UploadHealthTitle => ResolveUploadHealthTitle();

    public EdgeVisualStatus UploadHealthStatus => _uploadHealthStatus;

    public string UploadHealthStatusText => ResolveUploadHealthStatusText();

    public string LastUploadSuccessText => FormatTimestamp(_latestUploadSuccessAt);

    public string LastUploadFailureText => FormatTimestamp(_latestUploadFailureAt);

    public string UploadDeadLetterText => FormatCount(_deadLetterUploadCount);

    public bool IsUploadHealthDisabled => !_cloudUploadEnabled && !_mesUploadEnabled;

    public bool IsUploadHealthBodyVisible => !IsUploadHealthDisabled && UploadHealthSegments.Count > 0;

    public bool IsUploadHealthEmpty => !IsUploadHealthBodyVisible;

    public string UploadHealthEmptyTitle => IsUploadHealthDisabled
        ? GetText("Navigation_DashboardPreview_UploadDisabled", "上传未启用")
        : GetText("Navigation_DashboardPreview_UploadTrendEmptyTitle", "等待上传采样");

    public string UploadHealthEmptyMessage => IsUploadHealthDisabled
        ? GetText("Navigation_DashboardPreview_UploadDisabledMessage", "MES/云端上传均未启用。")
        : GetText("Navigation_DashboardPreview_UploadTrendEmptyMessage", "上传状态会按诊断采样显示。");

    public string AlertStateText => IsAlertEmpty
        ? GetText("Navigation_DashboardPreview_AlertNormal", "暂无告警")
        : GetText("Navigation_DashboardPreview_AlertActive", "有告警");

    public IReadOnlyList<EdgeSummaryItem> ProductionSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_Recipe", "配方"),
            Value = Normalize(_source.RecipeName)
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_PlcStatus", "PLC 状态"),
            Value = ConnectedDevices
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_RealtimeAlert", "报警态"),
            Value = AlertStateText
        }
    ];

    public bool IsAlertEmpty => AlertItems.Count == 0;

    public async Task OnActivatedAsync()
    {
        SubscribeLogStore();
        RefreshAlertsFromLogStore();
        _diagnosticsTimer.Start();
        await _source.OnActivatedAsync();
        await RefreshDiagnosticsAsync();
    }

    public async Task OnDeactivatedAsync()
    {
        _diagnosticsTimer.Stop();
        UnsubscribeLogStore();
        await _source.OnDeactivatedAsync();
    }

    public void Dispose()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Tick -= OnDiagnosticsTimerTick;
        _source.PropertyChanged -= OnSourcePropertyChanged;
        _languageService.LanguageChanged -= OnLanguageChanged;
        UnsubscribeLogStore();
    }

    private void SubscribeLogStore()
    {
        if (_isLogStoreSubscribed)
        {
            return;
        }

        _logDisplayStore.Entries.CollectionChanged += OnLogStoreEntriesChanged;
        _isLogStoreSubscribed = true;
    }

    private void UnsubscribeLogStore()
    {
        if (!_isLogStoreSubscribed)
        {
            return;
        }

        _logDisplayStore.Entries.CollectionChanged -= OnLogStoreEntriesChanged;
        _isLogStoreSubscribed = false;
    }

    private async void OnDiagnosticsTimerTick(object? sender, EventArgs e)
        => await RefreshDiagnosticsAsync();

    private async Task RefreshDiagnosticsAsync()
    {
        if (Interlocked.Exchange(ref _diagnosticsRefreshInFlight, 1) == 1)
        {
            return;
        }

        try
        {
            var diagnostics = await _diagnosticsQuery.GetCurrentAsync();
            var plcSnapshots = _plcConnectionManager.GetRuntimeStatuses();
            await AvaloniaDispatcher.UIThread.InvokeAsync(() => ApplyDiagnostics(diagnostics, plcSnapshots));
        }
        catch
        {
            // 总览降噪只读采样失败时保持现有显示，不反向写业务日志造成噪声。
        }
        finally
        {
            Interlocked.Exchange(ref _diagnosticsRefreshInFlight, 0);
        }
    }

    private void ApplyDiagnostics(
        EdgeSyncDiagnosticsSnapshot diagnostics,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> plcSnapshots)
    {
        ApplyRuntimeConfig(_runtimeConfig.Current);

        var cloudState = ResolveCloudState(diagnostics.Cloud);
        var mesState = ResolveMesState(diagnostics.Mes);
        var averagePlcLatency = ResolveAverageLatency(plcSnapshots);

        _cloudStateText = cloudState.StateText;
        _cloudProbeText = cloudState.ProbeText;
        _cloudStatus = cloudState.Status;
        _mesStateText = mesState.StateText;
        _mesProbeText = mesState.ProbeText;
        _mesStatus = mesState.Status;
        _plcLatencyText = FormatLatency(averagePlcLatency);
        _plcLatencyStatus = ResolveLatencyStatus(averagePlcLatency, plcSnapshots.Any(static x => x.IsConnected));
        _deviceLinksStatus = ResolveDeviceLinksStatus();

        UpdateUploadHealth(diagnostics, cloudState, mesState);

        OnPropertyChanged(nameof(CloudStateText));
        OnPropertyChanged(nameof(CloudProbeText));
        OnPropertyChanged(nameof(HasCloudProbeText));
        OnPropertyChanged(nameof(MesStateText));
        OnPropertyChanged(nameof(MesProbeText));
        OnPropertyChanged(nameof(HasMesProbeText));
        OnPropertyChanged(nameof(CloudLatencyText));
        OnPropertyChanged(nameof(MesLatencyText));
        OnPropertyChanged(nameof(PlcLatencyText));
        OnPropertyChanged(nameof(CloudLatencyStatus));
        OnPropertyChanged(nameof(MesLatencyStatus));
        OnPropertyChanged(nameof(CloudStatus));
        OnPropertyChanged(nameof(MesStatus));
        OnPropertyChanged(nameof(PlcLatencyStatus));
        OnPropertyChanged(nameof(DeviceLinksStatus));
        OnPropertyChanged(nameof(ProductionSummaryItems));
        NotifyUploadHealthChanged();
    }

    private void ApplyRuntimeConfig(SystemRuntimeConfigSnapshot runtimeConfig)
    {
        if (_cloudUploadEnabled == runtimeConfig.CloudUploadEnabled
            && _mesUploadEnabled == runtimeConfig.MesUploadEnabled)
        {
            return;
        }

        _cloudUploadEnabled = runtimeConfig.CloudUploadEnabled;
        _mesUploadEnabled = runtimeConfig.MesUploadEnabled;
        ResetUploadHealthTracking();
    }

    private void ResetUploadHealthTracking()
    {
        _lastCloudSuccessAt = null;
        _lastCloudFailureAt = null;
        _lastMesSuccessAt = null;
        _lastMesFailureAt = null;
        _latestUploadSuccessAt = null;
        _latestUploadFailureAt = null;
        _deadLetterUploadCount = 0;
        _uploadHealthStatus = EdgeVisualStatus.Offline;
        UploadChannelItems.Clear();
        UploadHealthSegments.Clear();
    }

    private void UpdateUploadHealth(
        EdgeSyncDiagnosticsSnapshot diagnostics,
        DashboardPreviewChannelState cloudState,
        DashboardPreviewChannelState mesState)
    {
        if (IsUploadHealthDisabled)
        {
            ResetUploadHealthTracking();
            return;
        }

        var cloudSuccessAt = _cloudUploadEnabled ? diagnostics.Cloud.LastSuccessAt : null;
        var cloudFailureAt = _cloudUploadEnabled ? diagnostics.Cloud.LastFailureAt : null;
        var mesSuccessAt = _mesUploadEnabled ? diagnostics.Mes.LastSuccessAt : null;
        var mesFailureAt = _mesUploadEnabled ? diagnostics.Mes.LastFailureAt : null;

        _latestUploadSuccessAt = Latest(cloudSuccessAt, mesSuccessAt);
        _latestUploadFailureAt = Latest(cloudFailureAt, mesFailureAt);
        _deadLetterUploadCount = ResolveDeadLetterUploadCount(diagnostics);
        _uploadHealthStatus = ResolveOverallUploadHealthStatus(diagnostics, cloudState, mesState);

        ReplaceUploadChannels(diagnostics, cloudState, mesState);
        AddUploadHealthSegment(_uploadHealthStatus);

        _lastCloudSuccessAt = cloudSuccessAt;
        _lastCloudFailureAt = cloudFailureAt;
        _lastMesSuccessAt = mesSuccessAt;
        _lastMesFailureAt = mesFailureAt;
    }

    private void ReplaceUploadChannels(
        EdgeSyncDiagnosticsSnapshot diagnostics,
        DashboardPreviewChannelState cloudState,
        DashboardPreviewChannelState mesState)
    {
        UploadChannelItems.Clear();

        UploadChannelItems.Add(CreateMesChannelItem(diagnostics.Mes, mesState));
        UploadChannelItems.Add(CreateCloudChannelItem(diagnostics.Cloud, cloudState));
    }

    private DashboardPreviewUploadChannelItem CreateCloudChannelItem(
        CloudSyncDiagnosticsSnapshot cloud,
        DashboardPreviewChannelState cloudState)
    {
        var stateText = ResolveUploadChannelStateText(
            cloudState,
            hasActiveFailure: HasActiveFailure(cloud.LastSuccessAt, cloud.LastFailureAt, cloud.DeadLetters?.TotalCount ?? 0));
        var status = ResolveUploadChannelStatus(
            cloudState,
            hasActiveFailure: HasActiveFailure(cloud.LastSuccessAt, cloud.LastFailureAt, cloud.DeadLetters?.TotalCount ?? 0));

        return new DashboardPreviewUploadChannelItem(
            GetText("Navigation_DashboardPreview_Cloud", "云端"),
            stateText,
            ResolveChannelDetail(cloudState),
            BuildCloudMetricItems(cloud),
            status);
    }

    private DashboardPreviewUploadChannelItem CreateMesChannelItem(
        MesSyncDiagnosticsSnapshot mes,
        DashboardPreviewChannelState mesState)
    {
        var stateText = ResolveUploadChannelStateText(
            mesState,
            hasActiveFailure: HasActiveFailure(mes.LastSuccessAt, mes.LastFailureAt, mes.DeadLetters?.TotalCount ?? 0)
                || mes.RuntimeState == MesRetryRuntimeState.LastFailed);
        var status = ResolveUploadChannelStatus(
            mesState,
            hasActiveFailure: HasActiveFailure(mes.LastSuccessAt, mes.LastFailureAt, mes.DeadLetters?.TotalCount ?? 0)
                || mes.RuntimeState == MesRetryRuntimeState.LastFailed);

        return new DashboardPreviewUploadChannelItem(
            GetText("Navigation_DashboardPreview_Mes", "MES"),
            stateText,
            ResolveChannelDetail(mesState),
            BuildMesMetricItems(mes),
            status);
    }

    private string ResolveUploadChannelStateText(DashboardPreviewChannelState channelState, bool hasActiveFailure)
    {
        if (!channelState.IsEnabled || !channelState.IsReady)
        {
            return channelState.StateText;
        }

        return hasActiveFailure
            ? GetText("Navigation_DashboardPreview_UploadFailure", "失败")
            : GetText("Navigation_DashboardPreview_UploadHealthy", "正常");
    }

    private static EdgeVisualStatus ResolveUploadChannelStatus(DashboardPreviewChannelState channelState, bool hasActiveFailure)
    {
        if (!channelState.IsEnabled)
        {
            return EdgeVisualStatus.Offline;
        }

        if (!channelState.IsReady || hasActiveFailure)
        {
            return EdgeVisualStatus.Error;
        }

        return EdgeVisualStatus.Running;
    }

    private string ResolveChannelDetail(DashboardPreviewChannelState channelState)
    {
        if (!channelState.IsEnabled)
        {
            return string.Empty;
        }

        if (!channelState.IsReady && !string.IsNullOrWhiteSpace(channelState.ProbeText))
        {
            return channelState.ProbeText;
        }

        return string.Empty;
    }

    private IReadOnlyList<DashboardPreviewUploadMetricItem> BuildCloudMetricItems(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (!_cloudUploadEnabled)
        {
            return [];
        }

        return
        [
            new(
                GetText("Navigation_DashboardPreview_CloudLogPending", "日志"),
                FormatCount(cloud.PendingDeviceLogCount)),
            new(
                GetText("Navigation_DashboardPreview_CloudPassStationPending", "过站"),
                FormatCount(cloud.PendingPassStationCount)),
            new(
                GetText("Navigation_DashboardPreview_CloudCapacityPending", "产能"),
                FormatCount(cloud.PendingCapacityCount)),
            new(
                GetText("Navigation_DashboardPreview_DeadLetters", "死信"),
                FormatCount(cloud.DeadLetters?.TotalCount ?? 0))
        ];
    }

    private IReadOnlyList<DashboardPreviewUploadMetricItem> BuildMesMetricItems(MesSyncDiagnosticsSnapshot mes)
    {
        if (!_mesUploadEnabled)
        {
            return [];
        }

        return
        [
            new(
                GetText("Navigation_DashboardPreview_MesRetryPending", "补传"),
                FormatCount(mes.PendingRetryCount)),
            new(
                GetText("Navigation_DashboardPreview_DeadLetters", "死信"),
                FormatCount(mes.DeadLetters?.TotalCount ?? 0))
        ];
    }

    private void AddUploadHealthSegment(EdgeVisualStatus status)
    {
        UploadHealthSegments.Add(new DashboardPreviewUploadHealthSegment(
            status,
            ResolveUploadHealthSegmentLabel(status)));

        while (UploadHealthSegments.Count > UploadHealthSegmentLimit)
        {
            UploadHealthSegments.RemoveAt(0);
        }
    }

    private DashboardPreviewChannelState ResolveCloudState(CloudSyncDiagnosticsSnapshot cloud)
    {
        if (!_cloudUploadEnabled)
        {
            return new(
                GetDisabledText(),
                string.Empty,
                EdgeVisualStatus.Offline,
                IsEnabled: false,
                IsReady: false);
        }

        if (cloud.GateState == EdgeUploadGateState.Ready && cloud.Heartbeat?.IsReady == true)
        {
            return new(
                FormatLatency(cloud.Heartbeat.LatencyMs),
                string.Empty,
                ResolveLatencyStatus(cloud.Heartbeat.LatencyMs, isReady: true),
                IsEnabled: true,
                IsReady: true);
        }

        return new(
            ResolveCloudNotReadyText(cloud),
            FormatProbeText(cloud.Heartbeat?.LatencyMs),
            EdgeVisualStatus.Offline,
            IsEnabled: true,
            IsReady: false);
    }

    private DashboardPreviewChannelState ResolveMesState(MesSyncDiagnosticsSnapshot mes)
    {
        if (!_mesUploadEnabled)
        {
            return new(
                GetDisabledText(),
                string.Empty,
                EdgeVisualStatus.Offline,
                IsEnabled: false,
                IsReady: false);
        }

        if (mes.Heartbeat?.IsReady == true)
        {
            return new(
                FormatLatency(mes.Heartbeat.LatencyMs),
                string.Empty,
                ResolveLatencyStatus(mes.Heartbeat.LatencyMs, isReady: true),
                IsEnabled: true,
                IsReady: true);
        }

        return new(
            GetText("Navigation_DashboardPreview_NotConnected", "未连接"),
            FormatProbeText(mes.Heartbeat?.LatencyMs),
            EdgeVisualStatus.Offline,
            IsEnabled: true,
            IsReady: false);
    }

    private string ResolveCloudNotReadyText(CloudSyncDiagnosticsSnapshot cloud)
        => cloud.BlockReason is EdgeUploadBlockReason.DeviceUnidentified
            or EdgeUploadBlockReason.MissingUploadToken
            or EdgeUploadBlockReason.ExpiredUploadToken
            or EdgeUploadBlockReason.UploadTokenRejected
            ? GetText("Navigation_DashboardPreview_NotActivated", "未激活")
            : GetText("Navigation_DashboardPreview_NotReady", "未就绪");

    private EdgeVisualStatus ResolveOverallUploadHealthStatus(
        EdgeSyncDiagnosticsSnapshot diagnostics,
        DashboardPreviewChannelState cloudState,
        DashboardPreviewChannelState mesState)
    {
        if (IsUploadHealthDisabled)
        {
            return EdgeVisualStatus.Offline;
        }

        if ((_cloudUploadEnabled && !cloudState.IsReady)
            || (_mesUploadEnabled && !mesState.IsReady))
        {
            return EdgeVisualStatus.Error;
        }

        if ((_cloudUploadEnabled && HasActiveFailure(diagnostics.Cloud.LastSuccessAt, diagnostics.Cloud.LastFailureAt, diagnostics.Cloud.DeadLetters?.TotalCount ?? 0))
            || (_mesUploadEnabled && HasActiveFailure(diagnostics.Mes.LastSuccessAt, diagnostics.Mes.LastFailureAt, diagnostics.Mes.DeadLetters?.TotalCount ?? 0))
            || (_mesUploadEnabled && diagnostics.Mes.RuntimeState == MesRetryRuntimeState.LastFailed))
        {
            return EdgeVisualStatus.Error;
        }

        return EdgeVisualStatus.Running;
    }

    private int ResolveDeadLetterUploadCount(EdgeSyncDiagnosticsSnapshot diagnostics)
    {
        var deadLetters = 0;
        if (_cloudUploadEnabled)
        {
            deadLetters += diagnostics.Cloud.DeadLetters?.TotalCount ?? 0;
        }

        if (_mesUploadEnabled)
        {
            deadLetters += diagnostics.Mes.DeadLetters?.TotalCount ?? 0;
        }

        return deadLetters;
    }

    private static bool HasActiveFailure(DateTime? lastSuccessAt, DateTime? lastFailureAt, int deadLetterCount)
        => deadLetterCount > 0
            || (lastFailureAt.HasValue && (!lastSuccessAt.HasValue || lastFailureAt.Value > lastSuccessAt.Value));

    private void OnLogStoreEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            RefreshAlertsFromLogStore();
            return;
        }

        AvaloniaDispatcher.UIThread.Post(RefreshAlertsFromLogStore, DispatcherPriority.Background);
    }

    private void RefreshAlertsFromLogStore()
    {
        var alerts = _logDisplayStore.Entries
            .Where(static x => IsAlertLevel(x.Level))
            .Take(AlertLimit)
            .Select(ToAlertItem)
            .ToArray();

        AlertItems.Clear();
        foreach (var alert in alerts)
        {
            AlertItems.Add(alert);
        }

        NotifyAlertsChanged();
    }

    private static DashboardPreviewAlertItem ToAlertItem(LogEntry entry)
        => new(
            entry.Time.ToString("HH:mm:ss"),
            entry.Level,
            entry.Message,
            EdgeVisualStatus.Error);

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(string.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshUploadHealthSegmentLabels();
        OnPropertyChanged(string.Empty);
        _ = RefreshDiagnosticsAsync();
    }

    private void RefreshUploadHealthSegmentLabels()
    {
        for (var i = 0; i < UploadHealthSegments.Count; i++)
        {
            var item = UploadHealthSegments[i];
            UploadHealthSegments[i] = item with { Label = ResolveUploadHealthSegmentLabel(item.Status) };
        }
    }

    private void NotifyAlertsChanged()
    {
        OnPropertyChanged(nameof(IsAlertEmpty));
        OnPropertyChanged(nameof(AlertStateText));
        OnPropertyChanged(nameof(ProductionSummaryItems));
    }

    private void NotifyUploadHealthChanged()
    {
        OnPropertyChanged(nameof(UploadHealthTitle));
        OnPropertyChanged(nameof(UploadHealthStatus));
        OnPropertyChanged(nameof(UploadHealthStatusText));
        OnPropertyChanged(nameof(LastUploadSuccessText));
        OnPropertyChanged(nameof(LastUploadFailureText));
        OnPropertyChanged(nameof(UploadDeadLetterText));
        OnPropertyChanged(nameof(IsUploadHealthDisabled));
        OnPropertyChanged(nameof(IsUploadHealthBodyVisible));
        OnPropertyChanged(nameof(IsUploadHealthEmpty));
        OnPropertyChanged(nameof(UploadHealthEmptyTitle));
        OnPropertyChanged(nameof(UploadHealthEmptyMessage));
    }

    private EdgeVisualStatus ResolveDeviceLinksStatus()
        => ConnectedDevices.StartsWith("0 /", StringComparison.Ordinal)
            ? EdgeVisualStatus.Offline
            : EdgeVisualStatus.Running;

    private EdgeVisualStatus ResolveLatencyStatus(int? latencyMs, bool isReady)
    {
        if (!isReady || latencyMs is null)
        {
            return EdgeVisualStatus.Offline;
        }

        return latencyMs > LatencyWarningThresholdMs ? EdgeVisualStatus.Warning : EdgeVisualStatus.Running;
    }

    private string FormatLatency(int? latencyMs)
        => latencyMs.HasValue
            ? FormatText("Navigation_DashboardPreview_LatencyMsFormat", "{0} ms", latencyMs.Value)
            : GetText("Navigation_DashboardPreview_LatencyUnknown", "—");

    private string FormatProbeText(int? latencyMs)
        => latencyMs is > 0
            ? FormatText("Navigation_DashboardPreview_ProbeLatencyFormat", "探测 {0} ms", latencyMs.Value)
            : string.Empty;

    private string FormatCount(int count)
        => FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", count);

    private string GetDisabledText()
        => GetText("Navigation_DashboardPreview_Disabled", "未启用");

    private int? ResolveAverageLatency(IReadOnlyCollection<PlcConnectionRuntimeSnapshot> snapshots)
    {
        var values = snapshots
            .Where(static x => x.IsConnected && x.LatencyMs.HasValue)
            .Select(static x => x.LatencyMs!.Value)
            .ToArray();

        return values.Length == 0
            ? null
            : (int)Math.Round(values.Average());
    }

    private string ResolveUploadHealthTitle()
    {
        if (_cloudUploadEnabled && _mesUploadEnabled)
        {
            return GetText("Navigation_DashboardPreview_UploadHealthCloudMes", "云端/MES 上传健康");
        }

        if (_cloudUploadEnabled)
        {
            return GetText("Navigation_DashboardPreview_UploadHealthCloud", "云端上传健康");
        }

        if (_mesUploadEnabled)
        {
            return GetText("Navigation_DashboardPreview_UploadHealthMes", "MES 上传健康");
        }

        return GetText("Navigation_DashboardPreview_UploadDisabled", "上传未启用");
    }

    private string ResolveUploadHealthStatusText()
    {
        if (IsUploadHealthDisabled)
        {
            return GetText("Navigation_DashboardPreview_UploadDisabled", "上传未启用");
        }

        return _uploadHealthStatus switch
        {
            EdgeVisualStatus.Running => GetText("Navigation_DashboardPreview_UploadHealthy", "正常"),
            EdgeVisualStatus.Error => GetText("Navigation_DashboardPreview_UploadFailure", "失败"),
            _ => GetText("Navigation_DashboardPreview_NoUploadEvent", "无上传事件")
        };
    }

    private string ResolveUploadHealthSegmentLabel(EdgeVisualStatus status)
        => status switch
        {
            EdgeVisualStatus.Running => GetText("Navigation_DashboardPreview_UploadHealthy", "正常"),
            EdgeVisualStatus.Error => GetText("Navigation_DashboardPreview_UploadFailure", "失败"),
            _ => GetText("Navigation_DashboardPreview_NoUploadEvent", "无上传事件")
        };

    private string FormatTimestamp(DateTime? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToString("HH:mm:ss")
            : EmptyValue;

    private string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => string.Format(GetText(key, fallback), args);

    private static DateTime? Latest(params DateTime?[] timestamps)
    {
        var values = timestamps
            .Where(static x => x.HasValue)
            .Select(static x => x!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Max();
    }

    private static bool IsAlertLevel(string? level)
        => string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "FATAL", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "--" ? EmptyValue : value;

    private sealed record DashboardPreviewChannelState(
        string StateText,
        string ProbeText,
        EdgeVisualStatus Status,
        bool IsEnabled,
        bool IsReady);
}

internal sealed class DashboardPreviewDesignViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly IAppLanguageService _languageService;

    public DashboardPreviewDesignViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;
        _languageService.LanguageChanged += OnLanguageChanged;
        ResetUploadHealthSegments();
        ResetUploadChannelItems();
    }

    public string RecentHourOutput => "188";

    public string RecentHourDescription => FormatText(
        "Navigation_DashboardPreview_RecentHourWindowFormat",
        "窗口：{0}",
        "13:40-14:40");

    public string ConnectedDevices => "8 / 8";

    public string CurrentBatch => "B20260521-01";

    public string CloudStateText => GetText("Navigation_DashboardPreview_NotActivated", "未激活");

    public string CloudProbeText => string.Empty;

    public bool HasCloudProbeText => false;

    public string MesStateText => GetText("Navigation_DashboardPreview_NotConnected", "未连接");

    public string MesProbeText => FormatText("Navigation_DashboardPreview_ProbeLatencyFormat", "探测 {0} ms", 3002);

    public bool HasMesProbeText => true;

    public string CloudLatencyText => CloudStateText;

    public string MesLatencyText => MesStateText;

    public string PlcLatencyText => "24 ms";

    public EdgeVisualStatus CloudLatencyStatus => EdgeVisualStatus.Offline;

    public EdgeVisualStatus MesLatencyStatus => EdgeVisualStatus.Offline;

    public EdgeVisualStatus CloudStatus => EdgeVisualStatus.Offline;

    public EdgeVisualStatus MesStatus => EdgeVisualStatus.Offline;

    public EdgeVisualStatus PlcLatencyStatus => EdgeVisualStatus.Running;

    public EdgeVisualStatus DeviceLinksStatus => EdgeVisualStatus.Running;

    public string UploadHealthTitle => GetText("Navigation_DashboardPreview_UploadHealthCloudMes", "云端/MES 上传健康");

    public EdgeVisualStatus UploadHealthStatus => EdgeVisualStatus.Error;

    public string UploadHealthStatusText => GetText("Navigation_DashboardPreview_UploadFailure", "失败");

    public string LastUploadSuccessText => "15:04:03";

    public string LastUploadFailureText => "15:04:17";

    public string UploadDeadLetterText => FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0);

    public bool IsUploadHealthDisabled => false;

    public bool IsUploadHealthBodyVisible => true;

    public bool IsUploadHealthEmpty => false;

    public string UploadHealthEmptyTitle => GetText("Navigation_DashboardPreview_UploadTrendEmptyTitle", "等待上传采样");

    public string UploadHealthEmptyMessage => GetText("Navigation_DashboardPreview_UploadTrendEmptyMessage", "上传状态会按诊断采样显示。");

    public ObservableCollection<DashboardPreviewAlertItem> AlertItems { get; } =
    [
        new("09:24", "ERROR", "PLC 返回报警状态。", EdgeVisualStatus.Error),
        new("09:02", "FATAL", "MES 上传连续失败。", EdgeVisualStatus.Error)
    ];

    public ObservableCollection<DashboardPreviewUploadChannelItem> UploadChannelItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadHealthSegment> UploadHealthSegments { get; } = [];

    public IReadOnlyList<EdgeSummaryItem> ProductionSummaryItems =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_Recipe", "配方"),
            Value = "浆料配方 V2.3"
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_PlcStatus", "PLC 状态"),
            Value = ConnectedDevices
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_RealtimeAlert", "报警态"),
            Value = GetText("Navigation_DashboardPreview_AlertActive", "有告警")
        }
    ];

    public bool IsAlertEmpty => false;

    public void Dispose()
        => _languageService.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ResetUploadHealthSegments();
        ResetUploadChannelItems();
        OnPropertyChanged(string.Empty);
    }

    private void ResetUploadHealthSegments()
    {
        UploadHealthSegments.Clear();
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, GetText("Navigation_DashboardPreview_UploadFailure", "失败")));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, GetText("Navigation_DashboardPreview_UploadFailure", "失败")));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Running, GetText("Navigation_DashboardPreview_UploadHealthy", "正常")));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, GetText("Navigation_DashboardPreview_UploadFailure", "失败")));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Running, GetText("Navigation_DashboardPreview_UploadHealthy", "正常")));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, GetText("Navigation_DashboardPreview_UploadFailure", "失败")));
    }

    private void ResetUploadChannelItems()
    {
        UploadChannelItems.Clear();
        UploadChannelItems.Add(new(
            GetText("Navigation_DashboardPreview_Mes", "MES"),
            GetText("Navigation_DashboardPreview_NotConnected", "未连接"),
            FormatText("Navigation_DashboardPreview_ProbeLatencyFormat", "探测 {0} ms", 3002),
            [
                new(GetText("Navigation_DashboardPreview_MesRetryPending", "补传"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0)),
                new(GetText("Navigation_DashboardPreview_DeadLetters", "死信"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0))
            ],
            EdgeVisualStatus.Error));
        UploadChannelItems.Add(new(
            GetText("Navigation_DashboardPreview_Cloud", "云端"),
            GetText("Navigation_DashboardPreview_NotActivated", "未激活"),
            string.Empty,
            [
                new(GetText("Navigation_DashboardPreview_CloudLogPending", "日志"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 130)),
                new(GetText("Navigation_DashboardPreview_CloudPassStationPending", "过站"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0)),
                new(GetText("Navigation_DashboardPreview_CloudCapacityPending", "产能"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0)),
                new(GetText("Navigation_DashboardPreview_DeadLetters", "死信"), FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", 0))
            ],
            EdgeVisualStatus.Offline));
    }

    private string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);

    private string FormatText(string key, string fallback, params object[] args)
        => string.Format(GetText(key, fallback), args);
}

internal sealed record DashboardPreviewAlertItem(
    string Time,
    string Level,
    string Message,
    EdgeVisualStatus Status);

internal sealed record DashboardPreviewUploadChannelItem(
    string Name,
    string StateText,
    string DetailText,
    IReadOnlyList<DashboardPreviewUploadMetricItem> Metrics,
    EdgeVisualStatus Status)
{
    public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

    public bool HasMetrics => Metrics.Count > 0;
}

internal sealed record DashboardPreviewUploadMetricItem(
    string Label,
    string Value);

internal sealed record DashboardPreviewUploadHealthSegment(
    EdgeVisualStatus Status,
    string Label)
{
    public bool IsSuccess => Status == EdgeVisualStatus.Running;

    public bool IsFailure => Status == EdgeVisualStatus.Error;

    public bool IsNeutral => !IsSuccess && !IsFailure;
}
