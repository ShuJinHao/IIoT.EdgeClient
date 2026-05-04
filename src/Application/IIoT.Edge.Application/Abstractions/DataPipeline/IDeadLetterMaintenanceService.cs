using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// 死信人工运维入口，负责列表、详情、重新入队和删除。
/// </summary>
public interface IDeadLetterMaintenanceService
{
    Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(DataPipelineRetryChannel channel, int count = 50);

    Task<DeadLetterRecord?> GetByIdAsync(DataPipelineRetryChannel channel, long id);

    Task<DeadLetterOperationResult> RequeueAsync(DataPipelineRetryChannel channel, long id);

    Task<DeadLetterOperationResult> DeleteAsync(DataPipelineRetryChannel channel, long id);
}

/// <summary>
/// 死信人工操作结果，消息用于直接显示给现场人员。
/// </summary>
public sealed record DeadLetterOperationResult(bool IsSuccess, string Message)
{
    public static DeadLetterOperationResult Success(string message) => new(true, message);

    public static DeadLetterOperationResult Failure(string message) => new(false, message);
}
