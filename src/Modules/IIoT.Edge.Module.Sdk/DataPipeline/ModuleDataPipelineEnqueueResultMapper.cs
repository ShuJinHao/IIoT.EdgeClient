using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Module.Sdk.DataPipeline;

/// <summary>
/// 将数据管道入队结果映射为模块任务结果，仅承载跨模块完全一致的文本语义。
/// </summary>
public static class ModuleDataPipelineEnqueueResultMapper
{
    /// <summary>
    /// 映射为“已进入目标队列 / 已进入溢出持久化 / 数据管道拒绝入队”语义。
    /// </summary>
    public static MesCallResult ToQueuedUploadResult(
        DataPipelineEnqueueResult enqueueResult,
        string scenarioName,
        DataPipelineUploadTargets uploadTargets)
        => Map(
            enqueueResult,
            acceptedMessage: $"{scenarioName}已进入 {DataPipelineUploadTargetPolicy.Format(uploadTargets)} 上传队列。",
            overflowMessage: $"{scenarioName}已接收，数据已进入溢出持久化。",
            rejectedMessageFactory: reason => $"{scenarioName}未接收，数据管道拒绝入队（{reason}）。");

    /// <summary>
    /// 映射为“已进入目标队列并等待后台上传 / 已进入溢出补偿 / 未进入上传队列”语义。
    /// </summary>
    public static MesCallResult ToPendingBackgroundUploadResult(
        DataPipelineEnqueueResult enqueueResult,
        string scenarioName,
        DataPipelineUploadTargets uploadTargets)
        => Map(
            enqueueResult,
            acceptedMessage: $"{scenarioName}已进入 {DataPipelineUploadTargetPolicy.Format(uploadTargets)} 上传队列，等待后台上传。",
            overflowMessage: $"{scenarioName}已进入溢出补偿，等待后台上传。",
            rejectedMessageFactory: reason => $"{scenarioName}未进入上传队列，原因={reason}。");

    private static MesCallResult Map(
        DataPipelineEnqueueResult enqueueResult,
        string acceptedMessage,
        string overflowMessage,
        Func<string, string> rejectedMessageFactory)
    {
        ArgumentNullException.ThrowIfNull(enqueueResult);

        if (enqueueResult.IsDurablyAccepted)
        {
            return MesCallResult.Success(enqueueResult.WasOverflow ? overflowMessage : acceptedMessage);
        }

        var reason = string.IsNullOrWhiteSpace(enqueueResult.ReasonCode)
            ? "unknown"
            : enqueueResult.ReasonCode;
        return MesCallResult.TransportFailure(rejectedMessageFactory(reason));
    }
}
