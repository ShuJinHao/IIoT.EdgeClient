using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Sdk.Cloud;

namespace IIoT.Edge.Application.Common.DataPipeline;

/// <summary>
/// Host 主队列的持久入口。入口记录与 Cloud/MES 通道库分离，
/// 只在所有适用消费者都已完成或可靠交接后清除完整信封。
/// </summary>
public interface IDataPipelineIngressStore
{
    Task<DataPipelineIngressAcceptance> AcceptAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default);

    Task<DataPipelineIngressRecord?> GetAsync(
        string completionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DataPipelineIngressRecord>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    Task MarkConsumerCompletedAsync(
        string completionId,
        string consumerKey,
        CancellationToken cancellationToken = default);

    Task<bool> CompleteIfAllConsumersFinishedAsync(
        string completionId,
        IReadOnlyCollection<string> requiredConsumerKeys,
        CancellationToken cancellationToken = default);
}

public sealed record DataPipelineIngressAcceptance(
    string CompletionId,
    CellCompletedRecord Record,
    bool AlreadyCompleted);

public sealed record DataPipelineIngressRecord(
    string CompletionId,
    CellCompletedRecord Record,
    IReadOnlySet<string> CompletedConsumerKeys);

/// <summary>
/// 完工事实的稳定入口身份。V2 算法不包含可变 DeviceName 和本地 NetworkDeviceId。
/// </summary>
public static class DataPipelineCompletionIdentity
{
    private const string IngressIdentityScope = "HostDurableIngress";

    public static string Create(CellCompletedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.CellData);
        return CloudIdempotencyKeyBuilder.ForRecord(
            record.CellData.ProcessType,
            IngressIdentityScope,
            record);
    }

    public static string CreateConsumerKey(
        DataPipelineRetryChannel channel,
        string consumerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        return $"{channel}:{consumerName.Trim()}".ToUpperInvariant();
    }
}
