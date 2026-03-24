// 路径：src/Tasks/IIoT.Edge.Task.Scan/Base/IBarcodeReader.cs
namespace IIoT.Edge.Tasks.Scan.Base;

/// <summary>
/// 条码读取统一接口
/// 
/// 实现类决定条码从哪里来：
///   PlcBarcodeReader — 从PLC实例直读（不走Buffer）
///   ScannerBarcodeReader — 从扫码枪实例读取
/// 
/// 返回数组：一个夹具上可能有N个电芯条码
/// </summary>
public interface IBarcodeReader
{
    /// <summary>
    /// 读取条码
    /// </summary>
    /// <returns>条码数组（1个或多个）</returns>
    Task<string[]> ReadAsync(CancellationToken ct = default);
}