using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using AvaloniaDispatcher = Avalonia.Threading.Dispatcher;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

internal sealed class DashboardPreviewRuntimeViewModel : DashboardPreviewLocalizedViewModel
{
    private const string EmptyValue = "—";
    private const int UploadHealthSegmentLimit = 6;
    private const int LatencyWarningThresholdMs = 5000;
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PlcConnectingDisplayTimeout = TimeSpan.FromSeconds(15);

    private readonly DashboardViewModel _source;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IMonitorConfiguredDeviceLoader _configuredDeviceLoader;
    private readonly DispatcherTimer _diagnosticsTimer;

    private DateTime? _lastCloudSuccessAt;
    private DateTime? _lastCloudFailureAt;
    private DateTime? _lastMesSuccessAt;
    private DateTime? _lastMesFailureAt;
    private DateTime? _latestUploadSuccessAt;
    private DateTime? _latestUploadFailureAt;
    private bool _systemCloudEnabled;
    private bool _mesUploadEnabled;
    private int _diagnosticsRefreshInFlight;
    private int _deadLetterUploadCount;
    private string _cloudStateText = EmptyValue;
    private string _cloudProbeText = string.Empty;
    private string _mesStateText = EmptyValue;
    private string _mesProbeText = string.Empty;
    private string _plcLatencyText = EmptyValue;
    private string _connectedDevicesText = EmptyValue;
    private string _plcFaultStateText = EmptyValue;
    private EdgeVisualStatus _cloudStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _mesStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _plcLatencyStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _deviceLinksStatus = EdgeVisualStatus.Offline;
    private EdgeVisualStatus _uploadHealthStatus = EdgeVisualStatus.Offline;
    private IReadOnlyCollection<PlcConnectionRuntimeSnapshot> _lastPlcSnapshots = [];
    private IReadOnlyCollection<NetworkDeviceEntity> _lastConfiguredPlcs = [];
    private DashboardPreviewPlcStatusItem? _selectedPlcStatusDetail;
    private bool _isPlcStatusDetailOpen;

    public DashboardPreviewRuntimeViewModel(
        DashboardViewModel source,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IPlcConnectionManager plcConnectionManager,
        IMonitorConfiguredDeviceLoader configuredDeviceLoader)
        : base(languageService)
    {
        _source = source;
        _deviceSelectionService = deviceSelectionService;
        _runtimeConfig = runtimeConfig;
        _diagnosticsQuery = diagnosticsQuery;
        _plcConnectionManager = plcConnectionManager;
        _configuredDeviceLoader = configuredDeviceLoader;
        _diagnosticsTimer = new DispatcherTimer { Interval = DiagnosticsRefreshInterval };
        _diagnosticsTimer.Tick += OnDiagnosticsTimerTick;

        var runtimeSnapshot = _runtimeConfig.Current;
        _systemCloudEnabled = runtimeSnapshot.SystemCloudEnabled;
        _mesUploadEnabled = runtimeSnapshot.MesUploadEnabled;

        _source.PropertyChanged += OnSourcePropertyChanged;
        _deviceSelectionService.SelectionChanged += OnSharedDeviceSelectionChanged;
        ShowPlcStatusDetailCommand = new BaseCommand(ShowPlcStatusDetail);
        ClosePlcStatusDetailCommand = new BaseCommand(_ => ClosePlcStatusDetail());
    }

    public ObservableCollection<DashboardPreviewPlcStatusItem> PlcStatusTableItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadChannelItem> UploadChannelItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadHealthSegment> UploadHealthSegments { get; } = [];

    public ICommand ShowPlcStatusDetailCommand { get; }

    public ICommand ClosePlcStatusDetailCommand { get; }

    public DashboardPreviewPlcStatusItem? SelectedPlcStatusDetail
    {
        get => _selectedPlcStatusDetail;
        private set
        {
            if (Equals(_selectedPlcStatusDetail, value))
            {
                return;
            }

            _selectedPlcStatusDetail = value;
            OnPropertyChanged();
        }
    }

    public bool IsPlcStatusDetailOpen
    {
        get => _isPlcStatusDetailOpen;
        private set
        {
            if (_isPlcStatusDetailOpen == value)
            {
                return;
            }

            _isPlcStatusDetailOpen = value;
            OnPropertyChanged();
        }
    }

    public string RecentHourOutput => Normalize(_source.RecentHourOutput);

    public string RecentHourDescription => FormatText(
        "Navigation_DashboardPreview_RecentHourWindowFormat",
        "窗口：{0}",
        Normalize(_source.RecentHourLabel));

    public string ConnectedDevices => _connectedDevicesText;

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

    public IReadOnlyList<EdgeSummaryItem> UploadHealthSummaryItems
        => BuildUploadHealthSummaryItems(
            LastUploadSuccessText,
            LastUploadFailureText,
            UploadDeadLetterText);

    public bool IsUploadHealthDisabled => !_systemCloudEnabled && !_mesUploadEnabled;

    public bool IsUploadHealthBodyVisible => !IsUploadHealthDisabled && UploadHealthSegments.Count > 0;

    public bool IsUploadHealthEmpty => !IsUploadHealthBodyVisible;

    public string UploadHealthEmptyTitle => IsUploadHealthDisabled
        ? GetText("Navigation_DashboardPreview_UploadDisabled", "上传未启用")
        : GetText("Navigation_DashboardPreview_UploadTrendEmptyTitle", "等待上传采样");

    public string UploadHealthEmptyMessage => IsUploadHealthDisabled
        ? GetText("Navigation_DashboardPreview_UploadDisabledMessage", "MES/云端上传均未启用。")
        : GetText("Navigation_DashboardPreview_UploadTrendEmptyMessage", "上传状态会按诊断采样显示。");

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
            Label = GetText("Navigation_DashboardPreview_PlcExceptions", "通讯异常"),
            Value = _plcFaultStateText
        }
    ];

    public bool IsPlcStatusTableEmpty => PlcStatusTableItems.Count == 0;

    public async Task OnActivatedAsync()
    {
        _diagnosticsTimer.Start();
        await _source.OnActivatedAsync();
        await RefreshDiagnosticsAsync();
    }

    public async Task OnDeactivatedAsync()
    {
        _diagnosticsTimer.Stop();
        await _source.OnDeactivatedAsync();
    }

    protected override void DisposeCore()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Tick -= OnDiagnosticsTimerTick;
        _source.PropertyChanged -= OnSourcePropertyChanged;
        _deviceSelectionService.SelectionChanged -= OnSharedDeviceSelectionChanged;
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
            var configuredPlcs = await _configuredDeviceLoader.LoadConfiguredPlcDevicesAsync(CancellationToken.None);
            await AvaloniaDispatcher.UIThread.InvokeAsync(() => ApplyDiagnostics(diagnostics, plcSnapshots, configuredPlcs));
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
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> plcSnapshots,
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs)
    {
        ApplyRuntimeConfig(_runtimeConfig.Current);

        var cloudState = ResolveCloudState(diagnostics.Cloud);
        var mesState = ResolveMesState(diagnostics.Mes);
        RefreshPlcStatusState(configuredPlcs, plcSnapshots, notify: false);

        _cloudStateText = cloudState.StateText;
        _cloudProbeText = cloudState.ProbeText;
        _cloudStatus = cloudState.Status;
        _mesStateText = mesState.StateText;
        _mesProbeText = mesState.ProbeText;
        _mesStatus = mesState.Status;

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
        OnPropertyChanged(nameof(ConnectedDevices));
        OnPropertyChanged(nameof(CloudLatencyStatus));
        OnPropertyChanged(nameof(MesLatencyStatus));
        OnPropertyChanged(nameof(CloudStatus));
        OnPropertyChanged(nameof(MesStatus));
        OnPropertyChanged(nameof(PlcLatencyStatus));
        OnPropertyChanged(nameof(DeviceLinksStatus));
        OnPropertyChanged(nameof(ProductionSummaryItems));
        NotifyUploadHealthChanged();
    }

    private void RefreshPlcStatusState(
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> plcSnapshots,
        bool notify)
    {
        _lastConfiguredPlcs = configuredPlcs;
        _lastPlcSnapshots = plcSnapshots;

        var selectedKey = _deviceSelectionService.SelectedDeviceKey;
        var isAllSelected = string.Equals(
            selectedKey,
            IDeviceSelectionService.AllFilterKey,
            StringComparison.OrdinalIgnoreCase);
        var projections = BuildPlcStatusProjections(configuredPlcs, plcSnapshots)
            .Where(snapshot =>
                isAllSelected
                || string.Equals(snapshot.DeviceName, selectedKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var averagePlcLatency = ResolveAverageLatency(projections);

        _connectedDevicesText = ResolveConnectedDevicesText(projections);
        _plcFaultStateText = ResolvePlcFaultStateText(projections);
        _plcLatencyText = FormatLatency(averagePlcLatency);
        _plcLatencyStatus = ResolveLatencyStatus(
            averagePlcLatency,
            projections.Any(static x => x.RuntimeSnapshot?.IsConnected == true));
        _deviceLinksStatus = ResolveDeviceLinksStatus(projections);
        PlcStatusTableItems.Clear();
        foreach (var projection in projections)
        {
            PlcStatusTableItems.Add(CreatePlcStatusItem(projection, !isAllSelected));
        }

        OnPropertyChanged(nameof(IsPlcStatusTableEmpty));
        if (!notify)
        {
            return;
        }

        OnPropertyChanged(nameof(ConnectedDevices));
        OnPropertyChanged(nameof(PlcLatencyText));
        OnPropertyChanged(nameof(PlcLatencyStatus));
        OnPropertyChanged(nameof(DeviceLinksStatus));
        OnPropertyChanged(nameof(ProductionSummaryItems));
    }

    private static IReadOnlyList<DashboardPreviewPlcStatusProjection> BuildPlcStatusProjections(
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> plcSnapshots)
    {
        var snapshotsById = plcSnapshots
            .Where(static snapshot => snapshot.NetworkDeviceId > 0)
            .GroupBy(static snapshot => snapshot.NetworkDeviceId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var snapshotsByName = plcSnapshots
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.DeviceName))
            .GroupBy(static snapshot => snapshot.DeviceName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        var projections = configuredPlcs
            .Where(static device => !string.IsNullOrWhiteSpace(device.DeviceName))
            .Select(device =>
            {
                snapshotsById.TryGetValue(device.Id, out var snapshot);
                if (snapshot is null)
                {
                    snapshotsByName.TryGetValue(device.DeviceName.Trim(), out snapshot);
                }

                return new DashboardPreviewPlcStatusProjection(device.Id, device.DeviceName.Trim(), device, snapshot);
            })
            .OrderBy(static projection => projection.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projections.Length > 0)
        {
            return projections;
        }

        return plcSnapshots
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.DeviceName))
            .Select(static snapshot => new DashboardPreviewPlcStatusProjection(
                snapshot.NetworkDeviceId,
                snapshot.DeviceName.Trim(),
                null,
                snapshot))
            .OrderBy(static projection => projection.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private DashboardPreviewPlcStatusItem CreatePlcStatusItem(
        DashboardPreviewPlcStatusProjection projection,
        bool isSelected)
    {
        var snapshot = projection.RuntimeSnapshot;
        if (snapshot is null)
        {
            return new DashboardPreviewPlcStatusItem(
                projection.DeviceName,
                GetText("Navigation_DashboardPreview_PlcStateUncollected", "未采集"),
                EdgeVisualStatus.Offline,
                EmptyValue,
                EmptyValue,
                GetText("Navigation_DashboardPreview_PlcNoError", "暂无运行错误"),
                FormatEndpoint(projection.ConfiguredDevice),
                Normalize(projection.ConfiguredDevice?.DeviceModel),
                Normalize(projection.ConfiguredDevice?.ProtocolFrame),
                EmptyValue,
                EmptyValue,
                EmptyValue,
                EmptyValue,
                isSelected);
        }

        var lastErrorDetail = string.IsNullOrWhiteSpace(snapshot.LastError) ? EmptyValue : snapshot.LastError.Trim();
        return new DashboardPreviewPlcStatusItem(
            projection.DeviceName,
            ResolvePlcConnectionStateText(snapshot),
            ResolvePlcVisualStatus(snapshot),
            snapshot.IsConnected && snapshot.LatencyMs.HasValue ? FormatLatency(snapshot.LatencyMs.Value) : EmptyValue,
            SummarizePlcError(snapshot.LastError),
            lastErrorDetail == EmptyValue
                ? GetText("Navigation_DashboardPreview_PlcNoError", "暂无运行错误")
                : lastErrorDetail,
            FormatEndpoint(projection.ConfiguredDevice),
            Normalize(projection.ConfiguredDevice?.DeviceModel),
            Normalize(projection.ConfiguredDevice?.ProtocolFrame),
            FormatTimestamp(snapshot.LastAttemptAtUtc),
            FormatTimestamp(snapshot.LastConnectedAtUtc),
            FormatTimestamp(snapshot.LastReadAtUtc),
            FormatTimestamp(snapshot.LastFailureAtUtc),
            isSelected);
    }

    private void ShowPlcStatusDetail(object? parameter)
    {
        if (parameter is not DashboardPreviewPlcStatusItem item)
        {
            return;
        }

        SelectedPlcStatusDetail = item;
        IsPlcStatusDetailOpen = true;
    }

    private void ClosePlcStatusDetail()
    {
        IsPlcStatusDetailOpen = false;
        SelectedPlcStatusDetail = null;
    }

    private void ApplyRuntimeConfig(SystemRuntimeConfigSnapshot runtimeConfig)
    {
        if (_systemCloudEnabled == runtimeConfig.SystemCloudEnabled
            && _mesUploadEnabled == runtimeConfig.MesUploadEnabled)
        {
            return;
        }

        _systemCloudEnabled = runtimeConfig.SystemCloudEnabled;
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

        var cloudSuccessAt = _systemCloudEnabled ? diagnostics.Cloud.LastSuccessAt : null;
        var cloudFailureAt = _systemCloudEnabled ? diagnostics.Cloud.LastFailureAt : null;
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
        if (!_systemCloudEnabled)
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
        if (!_systemCloudEnabled)
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

        if ((_systemCloudEnabled && !cloudState.IsReady)
            || (_mesUploadEnabled && !mesState.IsReady))
        {
            return EdgeVisualStatus.Error;
        }

        if ((_systemCloudEnabled && HasActiveFailure(diagnostics.Cloud.LastSuccessAt, diagnostics.Cloud.LastFailureAt, diagnostics.Cloud.DeadLetters?.TotalCount ?? 0))
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
        if (_systemCloudEnabled)
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

    private void OnSharedDeviceSelectionChanged(object? sender, EventArgs e)
    {
        if (AvaloniaDispatcher.UIThread.CheckAccess())
        {
            RefreshPlcStatusState(_lastConfiguredPlcs, _lastPlcSnapshots, notify: true);
            return;
        }

        AvaloniaDispatcher.UIThread.Post(
            () => RefreshPlcStatusState(_lastConfiguredPlcs, _lastPlcSnapshots, notify: true),
            DispatcherPriority.Background);
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(string.Empty);

    protected override void OnLanguageChanged()
    {
        RefreshUploadHealthSegmentLabels();
        RefreshPlcStatusState(_lastConfiguredPlcs, _lastPlcSnapshots, notify: true);
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

    private void NotifyUploadHealthChanged()
    {
        OnPropertyChanged(nameof(UploadHealthTitle));
        OnPropertyChanged(nameof(UploadHealthStatus));
        OnPropertyChanged(nameof(UploadHealthStatusText));
        OnPropertyChanged(nameof(LastUploadSuccessText));
        OnPropertyChanged(nameof(LastUploadFailureText));
        OnPropertyChanged(nameof(UploadDeadLetterText));
        OnPropertyChanged(nameof(UploadHealthSummaryItems));
        OnPropertyChanged(nameof(IsUploadHealthDisabled));
        OnPropertyChanged(nameof(IsUploadHealthBodyVisible));
        OnPropertyChanged(nameof(IsUploadHealthEmpty));
        OnPropertyChanged(nameof(UploadHealthEmptyTitle));
        OnPropertyChanged(nameof(UploadHealthEmptyMessage));
    }

    private EdgeVisualStatus ResolveDeviceLinksStatus(IReadOnlyCollection<DashboardPreviewPlcStatusProjection> projections)
    {
        if (projections.Count == 0)
        {
            return EdgeVisualStatus.Offline;
        }

        var connected = projections.Count(static x => x.RuntimeSnapshot?.IsConnected == true);
        if (connected == 0)
        {
            return EdgeVisualStatus.Offline;
        }

        return connected == projections.Count ? EdgeVisualStatus.Running : EdgeVisualStatus.Warning;
    }

    private string ResolveConnectedDevicesText(IReadOnlyCollection<DashboardPreviewPlcStatusProjection> projections)
        => projections.Count == 0
            ? EmptyValue
            : $"{projections.Count(static x => x.RuntimeSnapshot?.IsConnected == true)} / {projections.Count}";

    private string ResolvePlcFaultStateText(IReadOnlyCollection<DashboardPreviewPlcStatusProjection> projections)
    {
        if (projections.Count == 0)
        {
            return EmptyValue;
        }

        var collected = projections.Count(static x => x.RuntimeSnapshot is not null);
        if (collected == 0)
        {
            return GetText("Navigation_DashboardPreview_PlcStateUncollected", "未采集");
        }

        var faulted = projections.Count(static x => x.RuntimeSnapshot is { IsConnected: false });
        if (faulted == 0)
        {
            return collected == projections.Count
                ? GetText("Navigation_DashboardPreview_PlcHealthy", "正常")
                : GetText("Navigation_DashboardPreview_PlcPartiallyUncollected", "部分未采集");
        }

        return FormatText("Navigation_DashboardPreview_PlcFaultCountFormat", "{0}/{1} 异常", faulted, projections.Count);
    }

    private EdgeVisualStatus ResolvePlcVisualStatus(PlcConnectionRuntimeSnapshot snapshot)
        => IsConnectingTimedOut(snapshot)
            ? EdgeVisualStatus.Error
            : snapshot.ConnectionState switch
        {
            PlcConnectionState.Connected when snapshot.IsConnected => EdgeVisualStatus.Running,
            PlcConnectionState.Connecting or PlcConnectionState.Retrying => EdgeVisualStatus.Warning,
            PlcConnectionState.Faulted => EdgeVisualStatus.Error,
            _ => EdgeVisualStatus.Offline
        };

    private string ResolvePlcConnectionStateText(PlcConnectionRuntimeSnapshot snapshot)
    {
        if (IsConnectingTimedOut(snapshot))
        {
            return GetText("Navigation_DashboardPreview_PlcStateConnectionTimeout", "连接超时");
        }

        return snapshot.ConnectionState switch
        {
            PlcConnectionState.Connecting => GetText("Navigation_DashboardPreview_PlcStateConnecting", "连接中"),
            PlcConnectionState.Connected => GetText("Navigation_DashboardPreview_PlcStateConnected", "已连接"),
            PlcConnectionState.Retrying => GetText("Navigation_DashboardPreview_PlcStateRetrying", "重试中"),
            PlcConnectionState.Disconnected => GetText("Navigation_DashboardPreview_PlcStateDisconnected", "未连接"),
            PlcConnectionState.Faulted => GetText("Navigation_DashboardPreview_PlcStateFaulted", "异常"),
            _ => GetText("Navigation_DashboardPreview_PlcStateUnknown", "未知")
        };
    }

    private static bool IsConnectingTimedOut(PlcConnectionRuntimeSnapshot snapshot)
    {
        if (snapshot.IsConnected || snapshot.ConnectionState != PlcConnectionState.Connecting)
        {
            return false;
        }

        var lastAttempt = snapshot.LastAttemptAtUtc ?? snapshot.StateChangedAtUtc;
        return lastAttempt.HasValue
            && DateTimeOffset.UtcNow - lastAttempt.Value > PlcConnectingDisplayTimeout;
    }

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

    private string SummarizePlcError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return EmptyValue;
        }

        var normalized = error.Trim();
        if (normalized.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("超时", StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_DashboardPreview_PlcErrorTimeout", "通信超时");
        }

        if (normalized.Contains("缺少只读协议校验", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("协议校验", StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_DashboardPreview_PlcErrorProtocolProbeMissing", "缺少校验");
        }

        if (normalized.Contains("write", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("写入", StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_DashboardPreview_PlcErrorWriteFailure", "写入失败");
        }

        if (normalized.Contains("read", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("读取", StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_DashboardPreview_PlcErrorReadFailure", "读取失败");
        }

        if (normalized.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("连接", StringComparison.OrdinalIgnoreCase))
        {
            return GetText("Navigation_DashboardPreview_PlcErrorConnectFailure", "连接失败");
        }

        const int maxLength = 18;
        return normalized.Length <= maxLength ? normalized : normalized[..(maxLength - 1)] + "…";
    }

    private string FormatProbeText(int? latencyMs)
        => latencyMs is > 0
            ? FormatText("Navigation_DashboardPreview_ProbeLatencyFormat", "探测 {0} ms", latencyMs.Value)
            : string.Empty;

    private string GetDisabledText()
        => GetText("Navigation_DashboardPreview_Disabled", "未启用");

    private int? ResolveAverageLatency(IReadOnlyCollection<DashboardPreviewPlcStatusProjection> projections)
    {
        var values = projections
            .Select(static x => x.RuntimeSnapshot)
            .Where(static x => x is { IsConnected: true, LatencyMs: not null })
            .Select(static x => x!.LatencyMs!.Value)
            .ToArray();

        return values.Length == 0
            ? null
            : (int)Math.Round(values.Average());
    }

    private string ResolveUploadHealthTitle()
    {
        if (_systemCloudEnabled && _mesUploadEnabled)
        {
            return GetText("Navigation_DashboardPreview_UploadHealthCloudMes", "云端/MES 上传健康");
        }

        if (_systemCloudEnabled)
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
        => ResolveUploadHealthStatusText(_uploadHealthStatus, IsUploadHealthDisabled);

    private string FormatTimestamp(DateTime? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToString("HH:mm:ss")
            : EmptyValue;

    private string FormatTimestamp(DateTimeOffset? timestamp)
        => timestamp.HasValue
            ? timestamp.Value.ToLocalTime().ToString("HH:mm:ss")
            : EmptyValue;

    private static string FormatEndpoint(NetworkDeviceEntity? device)
        => device is null
            ? EmptyValue
            : $"{device.IpAddress}:{device.Port1}";

    private static DateTime? Latest(params DateTime?[] timestamps)
    {
        var values = timestamps
            .Where(static x => x.HasValue)
            .Select(static x => x!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Max();
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value == "--" ? EmptyValue : value;

    private sealed record DashboardPreviewChannelState(
        string StateText,
        string ProbeText,
        EdgeVisualStatus Status,
        bool IsEnabled,
        bool IsReady);

    private sealed record DashboardPreviewPlcStatusProjection(
        int NetworkDeviceId,
        string DeviceName,
        NetworkDeviceEntity? ConfiguredDevice,
        PlcConnectionRuntimeSnapshot? RuntimeSnapshot);
}

internal sealed class DashboardPreviewDesignViewModel : DashboardPreviewLocalizedViewModel
{
    public DashboardPreviewDesignViewModel(IAppLanguageService languageService)
        : base(languageService)
    {
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

    public string UploadDeadLetterText => FormatCount(0);

    public IReadOnlyList<EdgeSummaryItem> UploadHealthSummaryItems
        => BuildUploadHealthSummaryItems(
            LastUploadSuccessText,
            LastUploadFailureText,
            UploadDeadLetterText);

    public bool IsUploadHealthDisabled => false;

    public bool IsUploadHealthBodyVisible => true;

    public bool IsUploadHealthEmpty => false;

    public string UploadHealthEmptyTitle => GetText("Navigation_DashboardPreview_UploadTrendEmptyTitle", "等待上传采样");

    public string UploadHealthEmptyMessage => GetText("Navigation_DashboardPreview_UploadTrendEmptyMessage", "上传状态会按诊断采样显示。");

    public ObservableCollection<DashboardPreviewPlcStatusItem> PlcStatusTableItems { get; } =
    [
        new("P1-AP01", "已连接", EdgeVisualStatus.Running, "24 ms", "—", "暂无运行错误", "10.110.1.11:65531", "Mc", "E4", "15:04:01", "15:04:03", "15:04:03", "—", false),
        new("P1-AP02", "重试中", EdgeVisualStatus.Warning, "—", "读取失败", "Read R2450 failed.", "10.110.1.12:65531", "Mc", "E4", "15:04:12", "—", "—", "15:04:17", false)
    ];

    public ObservableCollection<DashboardPreviewUploadChannelItem> UploadChannelItems { get; } = [];

    public ObservableCollection<DashboardPreviewUploadHealthSegment> UploadHealthSegments { get; } = [];

    public ICommand ShowPlcStatusDetailCommand { get; } = new BaseCommand(_ => { });

    public ICommand ClosePlcStatusDetailCommand { get; } = new BaseCommand(_ => { });

    public DashboardPreviewPlcStatusItem? SelectedPlcStatusDetail => null;

    public bool IsPlcStatusDetailOpen => false;

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
            Label = GetText("Navigation_DashboardPreview_PlcExceptions", "通讯异常"),
            Value = FormatText("Navigation_DashboardPreview_PlcFaultCountFormat", "{0}/{1} 异常", 1, 8)
        }
    ];

    public bool IsPlcStatusTableEmpty => false;

    protected override void OnLanguageChanged()
    {
        ResetUploadHealthSegments();
        ResetUploadChannelItems();
        OnPropertyChanged(string.Empty);
    }

    private void ResetUploadHealthSegments()
    {
        UploadHealthSegments.Clear();
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Error)));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Error)));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Running, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Running)));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Error)));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Running, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Running)));
        UploadHealthSegments.Add(new(EdgeVisualStatus.Error, ResolveUploadHealthSegmentLabel(EdgeVisualStatus.Error)));
    }

    private void ResetUploadChannelItems()
    {
        UploadChannelItems.Clear();
        UploadChannelItems.Add(new(
            GetText("Navigation_DashboardPreview_Mes", "MES"),
            GetText("Navigation_DashboardPreview_NotConnected", "未连接"),
            FormatText("Navigation_DashboardPreview_ProbeLatencyFormat", "探测 {0} ms", 3002),
            [
                new(GetText("Navigation_DashboardPreview_MesRetryPending", "补传"), FormatCount(0)),
                new(GetText("Navigation_DashboardPreview_DeadLetters", "死信"), FormatCount(0))
            ],
            EdgeVisualStatus.Error));
        UploadChannelItems.Add(new(
            GetText("Navigation_DashboardPreview_Cloud", "云端"),
            GetText("Navigation_DashboardPreview_NotActivated", "未激活"),
            string.Empty,
            [
                new(GetText("Navigation_DashboardPreview_CloudLogPending", "日志"), FormatCount(130)),
                new(GetText("Navigation_DashboardPreview_CloudPassStationPending", "过站"), FormatCount(0)),
                new(GetText("Navigation_DashboardPreview_CloudCapacityPending", "产能"), FormatCount(0)),
                new(GetText("Navigation_DashboardPreview_DeadLetters", "死信"), FormatCount(0))
            ],
            EdgeVisualStatus.Offline));
    }

}

internal sealed record DashboardPreviewPlcStatusItem(
    string DeviceName,
    string StateText,
    EdgeVisualStatus Status,
    string LatencyText,
    string LastError,
    string LastErrorDetail,
    string EndpointText,
    string DeviceModelText,
    string ProtocolFrameText,
    string LastAttemptText,
    string LastConnectedText,
    string LastReadText,
    string LastFailureText,
    bool IsSelected)
{
    public bool HasLastErrorDetail => !string.IsNullOrWhiteSpace(LastErrorDetail) && LastErrorDetail != "—";
}

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
    string Label);
