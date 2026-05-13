using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Barcode.Readers;
using System.Text;

namespace IIoT.Edge.Infrastructure.DeviceComm.Barcode.Factories;

public sealed class PlcBarcodeReaderFactory : IBarcodeReaderFactory
{
    private readonly IPlcConnectionManager _plcConnectionManager;

    public PlcBarcodeReaderFactory(IPlcConnectionManager plcConnectionManager)
    {
        _plcConnectionManager = plcConnectionManager;
    }

    public IBarcodeReader Create(int networkDeviceId, PlcBarcodeReaderOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(networkDeviceId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.StartAddress);

        var plc = _plcConnectionManager.GetPlc(networkDeviceId);
        if (plc is null)
        {
            throw new InvalidOperationException(
                $"Cannot create barcode reader because PLC device '{networkDeviceId}' is not active.");
        }

        return new PlcBarcodeReader(
            plc,
            options.StartAddress,
            options.CodeCount,
            options.WordsPerCode,
            ResolveEncoding(options.EncodingName));
    }

    private static Encoding ResolveEncoding(string? encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName))
        {
            return Encoding.ASCII;
        }

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException)
        {
            return Encoding.ASCII;
        }
    }
}
