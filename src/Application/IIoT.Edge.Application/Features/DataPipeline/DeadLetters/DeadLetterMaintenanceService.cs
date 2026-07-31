using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Auth;
using System.Text.Json;

namespace IIoT.Edge.Application.Features.DataPipeline.DeadLetters;

/// <summary>
/// 死信人工处理服务。Cloud/MES 只复用操作流程，不复用存储链路。
/// </summary>
public sealed class DeadLetterMaintenanceService : IDeadLetterMaintenanceService
{
    private readonly ICloudDeadLetterStore _cloudDeadLetterStore;
    private readonly IMesDeadLetterStore _mesDeadLetterStore;
    private readonly ICloudRetryRecordStore _cloudRetryStore;
    private readonly IMesRetryRecordStore _mesRetryStore;
    private readonly ICloudDeadLetterRequeueStore _cloudRequeueStore;
    private readonly IMesDeadLetterRequeueStore _mesRequeueStore;
    private readonly IClientPermissionService _permissionService;
    private readonly IAuthService _authService;
    private readonly ILogService _logger;

    public DeadLetterMaintenanceService(
        ICloudDeadLetterStore cloudDeadLetterStore,
        IMesDeadLetterStore mesDeadLetterStore,
        ICloudRetryRecordStore cloudRetryStore,
        IMesRetryRecordStore mesRetryStore,
        ICloudDeadLetterRequeueStore cloudRequeueStore,
        IMesDeadLetterRequeueStore mesRequeueStore,
        IClientPermissionService permissionService,
        IAuthService authService,
        ILogService logger)
    {
        _cloudDeadLetterStore = cloudDeadLetterStore;
        _mesDeadLetterStore = mesDeadLetterStore;
        _cloudRetryStore = cloudRetryStore;
        _mesRetryStore = mesRetryStore;
        _cloudRequeueStore = cloudRequeueStore;
        _mesRequeueStore = mesRequeueStore;
        _permissionService = permissionService;
        _authService = authService;
        _logger = logger;
    }

    public Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(DataPipelineRetryChannel channel, int count = 50)
        => Resolve(channel).DeadLetterStore.GetLatestAsync(count);

    public Task<DeadLetterRecord?> GetByIdAsync(DataPipelineRetryChannel channel, long id)
        => Resolve(channel).DeadLetterStore.GetByIdAsync(id);

    public async Task<DeadLetterOperationResult> RequeueAsync(DataPipelineRetryChannel channel, long id)
    {
        if (!_permissionService.IsLocalAdmin
            || _authService.CurrentUser?.IsLocalAdmin != true)
        {
            return DeadLetterOperationResult.Failure("当前账号不是本地管理员，禁止死信重新入队。");
        }

        var operatorId = _authService.CurrentUser.EmployeeNo?.Trim();
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return DeadLetterOperationResult.Failure("本地管理员缺少可审计员工号，禁止死信重新入队。");
        }

        var stores = Resolve(channel);
        var record = await stores.DeadLetterStore.GetByIdAsync(id).ConfigureAwait(false);
        if (record is null)
        {
            return DeadLetterOperationResult.Failure($"未找到{stores.DisplayName}死信记录：{id}。");
        }

        var identityBlock = GetIdentityBlockReason(record);
        if (identityBlock is not null)
        {
            return DeadLetterOperationResult.Failure(
                $"{stores.DisplayName}死信身份未解析，原记录保留：{identityBlock}");
        }

        var businessIdentifier = ResolveBusinessIdentifier(record);
        var logPlcCode = NormalizeLogValue(record.PlcCode, "Unresolved");
        var logTaskKey = NormalizeLogValue(record.TaskKey, "Unresolved");
        var logBusinessIdentifier = NormalizeLogValue(businessIdentifier, $"deadletter-{id}");
        var logOperatorId = NormalizeLogValue(operatorId, "Unresolved");
        try
        {
            await stores.RequeueAndRemoveAsync(
                    id,
                    operatorId,
                    businessIdentifier,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"[CorrelationId=DeadLetter:{stores.LogPrefix}:{id}]" +
                $"[Channel={stores.LogPrefix}][PlcCode={logPlcCode}][TaskKey={logTaskKey}]" +
                $"[BusinessId={logBusinessIdentifier}][死信人工补传] 记录={id}，" +
                $"操作人={logOperatorId}，结果=Failed，异常类型={ex.GetType().Name}；原记录已保留。");
            return DeadLetterOperationResult.Failure(
                $"{stores.DisplayName}死信重新入队失败，原记录已保留。");
        }

        _logger.Warn(
            $"[CorrelationId=DeadLetter:{stores.LogPrefix}:{id}]" +
            $"[Channel={stores.LogPrefix}][PlcCode={logPlcCode}][TaskKey={logTaskKey}]" +
            $"[BusinessId={logBusinessIdentifier}][死信人工补传] 记录={id}，" +
            $"操作人={logOperatorId}，结果=Requeued；retry 写入与死信可消费源移除已在同一事务完成。");
        return DeadLetterOperationResult.Success($"{stores.DisplayName}死信已重新写入补传队列。");
    }

    public async Task<DeadLetterOperationResult> DeleteAsync(DataPipelineRetryChannel channel, long id)
    {
        var stores = Resolve(channel);
        _logger.Warn(
            $"[CorrelationId=DeadLetter:{stores.LogPrefix}:{id}]" +
            $"[Channel={stores.LogPrefix}][死信人工删除] 记录={id}，结果=Blocked，" +
            "原因码=deadletter_hard_delete_forbidden。");
        return DeadLetterOperationResult.Failure(
            $"{stores.DisplayName}死信尚未成功，禁止人工硬删除；只允许受控重新入队。");
    }

    private DeadLetterStores Resolve(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => new DeadLetterStores(
                _cloudDeadLetterStore,
                _cloudRequeueStore.RequeueAndRemoveAsync,
                "Cloud",
                "云端"),
            DataPipelineRetryChannel.Mes => new DeadLetterStores(
                _mesDeadLetterStore,
                _mesRequeueStore.RequeueAndRemoveAsync,
                "MES",
                "MES"),
            _ => throw new InvalidOperationException($"不支持的死信通道：{channel}。")
        };

    private static string? GetIdentityBlockReason(DeadLetterRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PlcCode))
        {
            return "PlcCode 为空";
        }

        return record.IdempotencyKeyVersion is
            CloudIdempotencyKeyVersion.LegacyV1 or CloudIdempotencyKeyVersion.PlcStableV2
            ? null
            : $"幂等版本 {record.IdempotencyKeyVersion} 无效";
    }

    private static string ResolveBusinessIdentifier(DeadLetterRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.CellDataJson);
            foreach (var propertyName in new[] { "clipNo", "barcode", "displayLabel" })
            {
                if (TryFindString(document.RootElement, propertyName, out var value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(record.TraceBatchNumber)
            ? $"deadletter-{record.Id}"
            : record.TraceBatchNumber;
    }

    private static bool TryFindString(JsonElement element, string propertyName, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    value = property.Value.GetString()!.Trim();
                    return true;
                }

                if (TryFindString(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindString(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeLogValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace(']', '_');
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private sealed record DeadLetterStores(
        IDeadLetterDiagnosticsStore DeadLetterStore,
        Func<long, string, string, CancellationToken, Task> RequeueAndRemoveAsync,
        string LogPrefix,
        string DisplayName);
}
