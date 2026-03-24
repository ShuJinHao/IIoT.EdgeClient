// 路径：src/Infrastructure/IIoT.Edge.Task.Scan/IScanTask.cs

// 路径：src/Infrastructure/IIoT.Edge.Task.Scan/IScanTask.cs
using IIoT.Edge.Tasks.Abstractions;

namespace IIoT.Edge.Tasks.Scan.Base;

/// <summary>
/// 扫码类任务能力接口
/// 所有扫码任务实现此接口，外部可统一查询扫码状态
/// </summary>
public interface IScanTask : IPlcHandshakeTask
{
    /// <summary>
    /// 最近一次扫到的条码
    /// </summary>
    string? LastScannedCode { get; }
}