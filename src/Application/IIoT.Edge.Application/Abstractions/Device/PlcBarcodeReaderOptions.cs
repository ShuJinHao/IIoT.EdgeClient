namespace IIoT.Edge.Application.Abstractions.Device;

public sealed class PlcBarcodeReaderOptions
{
    public required string StartAddress { get; init; }

    public int CodeCount { get; init; } = 1;

    public int WordsPerCode { get; init; } = 10;

    public string EncodingName { get; init; } = "ASCII";
}
