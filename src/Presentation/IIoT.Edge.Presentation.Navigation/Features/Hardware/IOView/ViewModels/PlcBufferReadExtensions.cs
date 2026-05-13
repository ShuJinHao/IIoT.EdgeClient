using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

internal static class PlcBufferReadExtensions
{
    public static ushort GetWriteBufferValue(this IPlcBuffer buffer, int index)
    {
        if (buffer is not IPlcBufferTransport transport)
        {
            return 0;
        }

        var snapshot = transport.GetWriteBuffer();
        return index >= 0 && index < snapshot.Length ? snapshot[index] : (ushort)0;
    }
}
