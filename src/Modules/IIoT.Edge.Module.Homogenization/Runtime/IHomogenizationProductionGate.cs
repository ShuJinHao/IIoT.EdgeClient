using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆生产前置门禁，只判断 MES 启用时主批计划和追溯批次号是否已经准备好。
/// </summary>
public interface IHomogenizationProductionGate
{
    /// <summary>
    /// 检查当前运行上下文是否允许继续进入生产相关上传链路。
    /// </summary>
    Task<MesCallResult> EnsureReadyAsync(
        HomogenizationContext context,
        CancellationToken cancellationToken = default);
}
