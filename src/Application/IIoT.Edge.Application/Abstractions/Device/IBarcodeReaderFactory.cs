namespace IIoT.Edge.Application.Abstractions.Device;

public interface IBarcodeReaderFactory
{
    IBarcodeReader Create(int networkDeviceId, PlcBarcodeReaderOptions options);
}
