using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Shell.Features.Footer;

public class FooterViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startTime = DateTime.Now;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private readonly IAppLanguageService _languageService;
    private string _deviceName;
    private string _cloudStatus;
    private Brush _cloudStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private string _mesStatus;
    private Brush _mesStatusColor = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private string _currentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
    private string _upTime = "00:00:00";
    private int _refreshInProgress;

    private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush RefreshingBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush OfflineBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    public override string ViewId => "Core.Footer";
    public override string ViewTitle => _languageService.GetString("Shell_ViewTitle_Footer", "页脚");

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            _deviceName = value;
            OnPropertyChanged();
        }
    }

    public string CloudStatus
    {
        get => _cloudStatus;
        set
        {
            _cloudStatus = value;
            OnPropertyChanged();
        }
    }

    public Brush CloudStatusColor
    {
        get => _cloudStatusColor;
        set
        {
            _cloudStatusColor = value;
            OnPropertyChanged();
        }
    }

    public string MesStatus
    {
        get => _mesStatus;
        set
        {
            _mesStatus = value;
            OnPropertyChanged();
        }
    }

    public Brush MesStatusColor
    {
        get => _mesStatusColor;
        set
        {
            _mesStatusColor = value;
            OnPropertyChanged();
        }
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set
        {
            _currentTime = value;
            OnPropertyChanged();
        }
    }

    public string UpTime
    {
        get => _upTime;
        private set
        {
            _upTime = value;
            OnPropertyChanged();
        }
    }

    static FooterViewModel()
    {
        OnlineBrush.Freeze();
        RefreshingBrush.Freeze();
        OfflineBrush.Freeze();
    }

    public FooterViewModel(
        IEdgeSyncDiagnosticsQuery diagnosticsQuery,
        IAppLanguageService languageService)
    {
        _diagnosticsQuery = diagnosticsQuery;
        _languageService = languageService;
        _deviceName = _languageService.GetString("Shell_Footer_Unknown", "未知");
        _cloudStatus = _languageService.GetString("Shell_Footer_CloudNotConnected", "云端：未连接");
        _mesStatus = _languageService.GetString("Shell_Footer_MesIdle", "MES：空闲");

        LayoutRow = 2;
        LayoutColumn = 0;
        ColumnSpan = 12;
        UpdateClock();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        _languageService.LanguageChanged += (_, _) => _ = SafeRefreshDiagnosticsAsync();
        _ = SafeRefreshDiagnosticsAsync();
    }

    internal Task RefreshDiagnosticsAsync(CancellationToken ct = default)
        => RefreshDiagnosticsIfIdleAsync(ct);

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateClock();
        await SafeRefreshDiagnosticsAsync();
    }

    private void UpdateClock()
    {
        CurrentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        var elapsed = DateTime.Now - _startTime;
        UpTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private async Task SafeRefreshDiagnosticsAsync(CancellationToken ct = default)
    {
        try
        {
            await RefreshDiagnosticsIfIdleAsync(ct);
        }
        catch
        {
            // 页脚诊断刷新失败不应中断界面时钟和状态轮询。
        }
    }

    private async Task RefreshDiagnosticsIfIdleAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _refreshInProgress, 1) == 1)
        {
            return;
        }

        try
        {
            await RefreshDiagnosticsCoreAsync(ct);
        }
        finally
        {
            Volatile.Write(ref _refreshInProgress, 0);
        }
    }

    private async Task RefreshDiagnosticsCoreAsync(CancellationToken ct)
    {
        var snapshot = await _diagnosticsQuery.GetCurrentAsync(ct);
        DeviceName = snapshot.DeviceName;

        CloudStatus = FormatCloudFooterStatus(snapshot.Cloud);
        CloudStatusColor = snapshot.Cloud switch
        {
            _ when snapshot.Cloud.IsPersistenceFaulted => OfflineBrush,
            _ when snapshot.Cloud.IsCapacityBlocked => OfflineBrush,
            _ when snapshot.Cloud.GateState == EdgeUploadGateState.Ready => OnlineBrush,
            _ when snapshot.Cloud.IsPausedWaitingForRecovery => RefreshingBrush,
            _ => OfflineBrush
        };

        MesStatus = FormatMesFooterStatus(snapshot.Mes);
        MesStatusColor = snapshot.Mes.RuntimeState switch
        {
            _ when snapshot.Mes.IsPersistenceFaulted => OfflineBrush,
            _ when snapshot.Mes.IsCapacityBlocked => OfflineBrush,
            MesRetryRuntimeState.Retrying => OnlineBrush,
            MesRetryRuntimeState.Idle => OnlineBrush,
            MesRetryRuntimeState.Backoff => RefreshingBrush,
            _ => OfflineBrush
        };
    }

    private string FormatCloudFooterStatus(CloudSyncDiagnosticsSnapshot snapshot)
    {
        if (snapshot.IsPersistenceFaulted)
        {
            return _languageService.GetString("Shell_Footer_CloudPersistenceFault", "云端：存储故障");
        }

        if (snapshot.IsCapacityBlocked)
        {
            return _languageService.GetString("Shell_Footer_CloudCapacityBlocked", "云端：产能阻塞");
        }

        if (snapshot.GateState == EdgeUploadGateState.Ready)
        {
            return _languageService.GetString("Shell_Footer_CloudReady", "云端：已就绪");
        }

        if (snapshot.IsPausedWaitingForRecovery)
        {
            return _languageService.GetString("Shell_Footer_CloudWaitingRecovery", "云端：等待恢复");
        }

        return _languageService.Format(
            "Shell_Footer_CloudBlockedFormat",
            "云端：已阻塞（{0}）",
            FormatBlockReason(snapshot.BlockReason));
    }

    private string FormatMesFooterStatus(MesSyncDiagnosticsSnapshot snapshot) => snapshot.RuntimeState switch
    {
        _ when snapshot.IsPersistenceFaulted => _languageService.GetString("Shell_Footer_MesPersistenceFault", "MES：存储故障"),
        _ when snapshot.IsCapacityBlocked => _languageService.GetString("Shell_Footer_MesCapacityBlocked", "MES：产能阻塞"),
        MesRetryRuntimeState.Retrying => _languageService.GetString("Shell_Footer_MesRetrying", "MES：重试中"),
        MesRetryRuntimeState.Backoff => _languageService.GetString("Shell_Footer_MesBackoff", "MES：退避中"),
        MesRetryRuntimeState.LastFailed => _languageService.GetString("Shell_Footer_MesLastFailed", "MES：最近失败"),
        _ => _languageService.GetString("Shell_Footer_MesIdle", "MES：空闲")
    };

    private string FormatBlockReason(EdgeUploadBlockReason reason) => reason switch
    {
        EdgeUploadBlockReason.None => _languageService.GetString("Shell_BlockReason_None", "无"),
        EdgeUploadBlockReason.DeviceUnidentified => _languageService.GetString("Shell_BlockReason_DeviceUnidentified", "设备未识别"),
        EdgeUploadBlockReason.MissingUploadToken => _languageService.GetString("Shell_BlockReason_MissingUploadToken", "缺少上传令牌"),
        EdgeUploadBlockReason.ExpiredUploadToken => _languageService.GetString("Shell_BlockReason_ExpiredUploadToken", "上传令牌已过期"),
        EdgeUploadBlockReason.BootstrapHttpFailure => _languageService.GetString("Shell_BlockReason_BootstrapHttpFailure", "bootstrap HTTP 失败"),
        EdgeUploadBlockReason.BootstrapTimeout => _languageService.GetString("Shell_BlockReason_BootstrapTimeout", "bootstrap 超时"),
        EdgeUploadBlockReason.BootstrapNetworkFailure => _languageService.GetString("Shell_BlockReason_BootstrapNetworkFailure", "bootstrap 网络失败"),
        EdgeUploadBlockReason.BootstrapPayloadInvalid => _languageService.GetString("Shell_BlockReason_BootstrapPayloadInvalid", "bootstrap 响应无效"),
        EdgeUploadBlockReason.UploadTokenRejected => _languageService.GetString("Shell_BlockReason_UploadTokenRejected", "上传令牌被拒绝"),
        _ => _languageService.GetString("Shell_Footer_Unknown", "未知")
    };
}
