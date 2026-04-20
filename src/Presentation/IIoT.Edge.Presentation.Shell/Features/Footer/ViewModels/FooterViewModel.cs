using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Shell.Features.Footer;

public class FooterViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startTime = DateTime.Now;
    private readonly IEdgeSyncDiagnosticsQuery _diagnosticsQuery;
    private string _deviceName = "Unknown";
    private string _cloudStatus = "Cloud: Blocked (device)";
    private Brush _cloudStatusColor = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
    private string _mesStatus = "MES: Idle";
    private Brush _mesStatusColor = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private string _currentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
    private string _upTime = "00:00:00";
    private int _refreshInProgress;

    private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush RefreshingBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush OfflineBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));

    public override string ViewId => "Core.Footer";
    public override string ViewTitle => "Footer";

    public string DeviceName
    {
        get => _deviceName;
        set { _deviceName = value; OnPropertyChanged(); }
    }

    public string CloudStatus
    {
        get => _cloudStatus;
        set { _cloudStatus = value; OnPropertyChanged(); }
    }

    public Brush CloudStatusColor
    {
        get => _cloudStatusColor;
        set { _cloudStatusColor = value; OnPropertyChanged(); }
    }

    public string MesStatus
    {
        get => _mesStatus;
        set { _mesStatus = value; OnPropertyChanged(); }
    }

    public Brush MesStatusColor
    {
        get => _mesStatusColor;
        set { _mesStatusColor = value; OnPropertyChanged(); }
    }

    public string CurrentTime
    {
        get => _currentTime;
        private set { _currentTime = value; OnPropertyChanged(); }
    }

    public string UpTime
    {
        get => _upTime;
        private set { _upTime = value; OnPropertyChanged(); }
    }

    static FooterViewModel()
    {
        OnlineBrush.Freeze();
        RefreshingBrush.Freeze();
        OfflineBrush.Freeze();
    }

    public FooterViewModel(IEdgeSyncDiagnosticsQuery diagnosticsQuery)
    {
        _diagnosticsQuery = diagnosticsQuery;

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
            // Diagnostics failures should not tear down the UI refresh loop.
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

        CloudStatus = EdgeSyncDiagnosticsFormatter.FormatCloudFooterStatus(snapshot.Cloud);
        CloudStatusColor = snapshot.Cloud switch
        {
            _ when snapshot.Cloud.IsPersistenceFaulted => OfflineBrush,
            _ when snapshot.Cloud.IsCapacityBlocked => OfflineBrush,
            _ when snapshot.Cloud.GateState == EdgeUploadGateState.Ready => OnlineBrush,
            _ when snapshot.Cloud.IsPausedWaitingForRecovery => RefreshingBrush,
            _ => OfflineBrush
        };

        MesStatus = EdgeSyncDiagnosticsFormatter.FormatMesFooterStatus(snapshot.Mes);
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
}
