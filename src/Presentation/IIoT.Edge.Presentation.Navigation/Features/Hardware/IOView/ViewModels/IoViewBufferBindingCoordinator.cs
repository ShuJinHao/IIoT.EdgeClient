using IIoT.Edge.Application.Abstractions.Plc.Store;
using Avalonia.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public interface IIoViewBufferBindingCoordinator
{
    void Bind(int networkDeviceId, Action refreshValues);

    void Unbind();
}

public sealed class IoViewBufferBindingCoordinator(IPlcDataStore dataStore) : IIoViewBufferBindingCoordinator
{
    private IPlcBufferTransport? _selectedBuffer;
    private Action? _refreshValues;

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

        _refreshValues = null;
    }

    private void OnBufferSignalValuesChanged(object? sender, PlcSignalBufferChangedEventArgs e)
    {
        var refresh = _refreshValues;
        if (refresh is null)
        {
            return;
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            refresh();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(refresh);
    }
}
