using System.Globalization;
using IIoT.Edge.Application.Abstractions.Plc.Store;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public interface IIoViewInteractionWriter
{
    void Write(int networkDeviceId, IoInteractionRowModel row);
}

public sealed class IoViewInteractionWriter(IPlcDataStore dataStore) : IIoViewInteractionWriter
{
    public void Write(int networkDeviceId, IoInteractionRowModel row)
    {
        if (row.HostSignals.Count == 0)
        {
            return;
        }

        var buffer = dataStore.GetBuffer(networkDeviceId);
        if (buffer is null)
        {
            return;
        }

        var displayValue = row.WriteValue.ToString(CultureInfo.InvariantCulture);
        foreach (var signal in row.HostSignals)
        {
            buffer.SetWriteValue(signal.SignalKey, 0, unchecked((ushort)row.WriteValue));
            buffer.SetWriteValue(signal.StartIndex, unchecked((ushort)row.WriteValue));
            signal.DisplayValue = displayValue;
            signal.PreviewValue = displayValue;
        }

        row.NotifyValuesChanged();
    }
}
