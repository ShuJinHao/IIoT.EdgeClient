using IIoT.Edge.Runtime.Abstractions;

namespace IIoT.Edge.Runtime.Scan.Base;

/// <summary>
/// 扫码类任务能力接口。
/// </summary>
public interface IScanTask : IPlcHandshakeTask
{
    /// <summary>
    /// 最近一次扫到的条码。
    /// </summary>
    string? LastScannedCode { get; }
}
