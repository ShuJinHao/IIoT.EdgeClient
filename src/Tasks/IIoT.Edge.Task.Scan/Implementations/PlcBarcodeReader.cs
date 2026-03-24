// 路径：src/Tasks/IIoT.Edge.Task.Scan/Implementations/PlcBarcodeReader.cs
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Tasks.Scan.Base;
using System.Text;

namespace IIoT.Edge.Tasks.Scan.Implementations;

/// <summary>
/// 从PLC实例直读条码（不走Buffer，按需读取）
/// 
/// 支持一次读取多个条码（如一个夹具64个电芯）
/// 每个条码在PLC中占固定长度的连续word地址
/// </summary>
public class PlcBarcodeReader : IBarcodeReader
{
    private readonly IPlcService _plcService;
    private readonly string _startAddress;     // 条码区起始PLC地址（如 "D1000"）
    private readonly int _codeCount;           // 条码个数（1个夹具N个电芯）
    private readonly int _wordsPerCode;        // 每个条码占多少个word
    private readonly Encoding _encoding;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="plcService">PLC通信实例</param>
    /// <param name="startAddress">条码区起始地址</param>
    /// <param name="codeCount">条码个数</param>
    /// <param name="wordsPerCode">每个条码占用的word数</param>
    /// <param name="encoding">编码方式，默认ASCII</param>
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
        // 一次性读取全部条码区域
        var totalWords = (ushort)(_codeCount * _wordsPerCode);
        var rawData = await _plcService
            .ReadDataAsync<ushort>(_startAddress, totalWords)
            .ConfigureAwait(false);

        var barcodes = new string[_codeCount];

        for (int i = 0; i < _codeCount; i++)
        {
            var offset = i * _wordsPerCode;
            var bytes = new byte[_wordsPerCode * 2];

            for (int j = 0; j < _wordsPerCode; j++)
            {
                bytes[j * 2] = (byte)(rawData[offset + j] & 0xFF);
                bytes[j * 2 + 1] = (byte)(rawData[offset + j] >> 8);
            }

            barcodes[i] = _encoding.GetString(bytes).TrimEnd('\0').Trim();
        }

        return barcodes;
    }
}