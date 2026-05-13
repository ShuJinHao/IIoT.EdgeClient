using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Plc;
using System.Text;

namespace IIoT.Edge.Infrastructure.DeviceComm.Barcode.Readers;

public class PlcBarcodeReader : IBarcodeReader
{
    private readonly IPlcService _plcService;
    private readonly string _startAddress;
    private readonly int _codeCount;
    private readonly int _wordsPerCode;
    private readonly Encoding _encoding;

    public PlcBarcodeReader(
        IPlcService plcService,
        string startAddress,
        int codeCount = 1,
        int wordsPerCode = 10,
        Encoding? encoding = null)
    {
        _plcService = plcService;
        _startAddress = startAddress;
        _codeCount = codeCount;
        _wordsPerCode = wordsPerCode;
        _encoding = encoding ?? Encoding.ASCII;
    }

    public async Task<string[]> ReadAsync(CancellationToken ct = default)
    {
        var totalWords = (ushort)(_codeCount * _wordsPerCode);
        var rawData = await _plcService
            .ReadDataAsync<ushort>(_startAddress, totalWords)
            .WaitAsync(ct)
            .ConfigureAwait(false);
        var barcodes = new string[_codeCount];

        for (var i = 0; i < _codeCount; i++)
        {
            var offset = i * _wordsPerCode;
            var bytes = new byte[_wordsPerCode * 2];

            for (var j = 0; j < _wordsPerCode; j++)
            {
                bytes[j * 2] = (byte)(rawData[offset + j] & 0xFF);
                bytes[j * 2 + 1] = (byte)(rawData[offset + j] >> 8);
            }

            barcodes[i] = _encoding.GetString(bytes).TrimEnd('\0').Trim();
        }

        return barcodes;
    }
}
