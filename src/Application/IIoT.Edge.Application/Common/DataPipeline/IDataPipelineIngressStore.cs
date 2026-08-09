using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Sdk.Cloud;
using System.Security.Cryptography;
using System.Text;

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
/// Stable durable-ingress identity. V3 isolates an explicit completion fact by ClientCode;
/// legacy records continue using the immutable V2 algorithm without silent re-keying.
/// </summary>
public static class DataPipelineCompletionIdentity
{
    private const string IngressIdentityScope = "HostDurableIngress";

    public static string Create(CellCompletedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.CellData);
        if (!string.IsNullOrWhiteSpace(record.ClientCode)
            || !string.IsNullOrWhiteSpace(record.CompletionId)
            || !string.IsNullOrWhiteSpace(record.TypeKey))
        {
            var clientCode = IIoT.Edge.SharedKernel.Configuration.EdgeClientIdentity.NormalizeClientCode(
                record.ClientCode);
            var completionId = RequireToken(record.CompletionId, nameof(record.CompletionId), 256);
            _ = RequireToken(record.TypeKey, nameof(record.TypeKey), 128);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"V3\n{clientCode}\n{completionId}"));
            return $"V3:{Convert.ToHexString(bytes).ToLowerInvariant()}";
        }

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

    private static string RequireToken(string value, string name, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > maximumLength
            || normalized.Any(char.IsControl))
        {
            throw new InvalidDataException($"{name} is required for a v3 completion record.");
        }

        return normalized;
    }
}
