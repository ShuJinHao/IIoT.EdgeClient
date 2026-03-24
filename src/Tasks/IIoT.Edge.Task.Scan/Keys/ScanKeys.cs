// 路径：src/Infrastructure/IIoT.Edge.Task.Scan/Keys/ScanKeys.cs
namespace IIoT.Edge.Tasks.Scan.Keys;

/// <summary>
/// 扫码服务写入 ProductionContext 的 Key 常量
/// 
/// 设备级：
///   LastScanBarcode — 最近一次扫到的条码（调试/UI展示用）
/// 
/// 电芯级（按条码隔离）：
///   ScanTime — 该电芯的扫码时间
///   ScanSource — 扫码来源（上料/出料/复检...）
///   ScanResult — 扫码结果（true=OK, false=NG）
/// </summary>
public static class ScanKeys
{
    // ── 设备级 ──
    public const string LastScanBarcode = "scan.last_barcode";
    public const string LastScanTime = "scan.last_time";

    // ── 电芯级 ──
    public const string ScanTime = "scan.time";
    public const string ScanSource = "scan.source";
    public const string ScanResult = "scan.result";
    public const string ScanMessage = "scan.message";
}