using IIoT.Edge.Module.Contracts.Plc.Store;
using Avalonia.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public interface IIoViewBufferBindingCoordinator
{
    void Bind(int networkDeviceId, Action refreshValues);

    void Unbind();
}

public sealed class IoViewBufferBindingCoordinator(IPlcDataStore dataStore) : IIoViewBufferBindingCoordinator
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private IPlcBufferTransport? _selectedBuffer;
    private Action? _refreshValues;
    private DispatcherTimer? _refreshTimer;
    private bool _refreshPending;

    public void Bind(int networkDeviceId, Action refreshValues)
    {
        Unbind();
        _refreshValues = refreshValues;
        _selectedBuffer = dataStore.GetBuffer(networkDeviceId);
        if (_selectedBuffer is not null)
        {
            _selectedBuffer.SignalValuesChanged += OnBufferSignalValuesChanged;
        }
    }

    public void Unbind()
    {
        if (_selectedBuffer is not null)
        {
            _selectedBuffer.SignalValuesChanged -= OnBufferSignalValuesChanged;
            _selectedBuffer = null;
        }

        StopRefreshTimer();
        _refreshPending = false;
        _refreshValues = null;
    }

    private void OnBufferSignalValuesChanged(object? sender, PlcSignalBufferChangedEventArgs e)
    {
        if (_refreshValues is null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RequestRefresh();
            return;
        }

        Dispatcher.UIThread.Post(RequestRefresh, DispatcherPriority.Background);
    }

    private void RequestRefresh()
    {
        if (_refreshValues is null)
        {
            return;
        }

        _refreshPending = true;
        _refreshTimer ??= CreateRefreshTimer();
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }
    }

    private DispatcherTimer CreateRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval
        };
        timer.Tick += OnRefreshTimerTick;
        return timer;
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!_refreshPending)
        {
            StopRefreshTimer();
            return;
        }

        _refreshPending = false;
        _refreshValues?.Invoke();

        if (!_refreshPending)
        {
            StopRefreshTimer();
        }
    }

    private void StopRefreshTimer()
    {
        if (_refreshTimer is null)
        {
            return;
        }

        _refreshTimer.Stop();
    }
}
