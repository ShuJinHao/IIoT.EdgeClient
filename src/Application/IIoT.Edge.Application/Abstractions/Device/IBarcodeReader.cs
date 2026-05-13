namespace IIoT.Edge.Application.Abstractions.Device;

public interface IBarcodeReader
{
    Task<string[]> ReadAsync(CancellationToken cancellationToken = default);
}
