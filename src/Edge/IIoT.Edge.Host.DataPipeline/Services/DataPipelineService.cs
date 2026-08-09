using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Common.Identity;
using System.Threading.Channels;

namespace IIoT.Edge.Host.DataPipeline.Services;

public class DataPipelineService : IDataPipelineService
{
    private const int QueueCapacity = 5000;

    private readonly Channel<CellCompletedRecord> _queue;
    private readonly IIngressOverflowPersistence? _legacyOverflowPersistence;
    private readonly IDataPipelineIngressStore? _ingressStore;
    private readonly ILogService _logger;
    private readonly IDevicePluginRuntimeContext? _runtimeContext;
    private int _pendingCount;
    private int _overflowCount;
    private int _spillCount;

    public DataPipelineService(
        IIngressOverflowPersistence overflowPersistence,
        ILogService logger)
        : this(overflowPersistence, logger, ingressStore: null)
    {
    }

    public DataPipelineService(
        ILogService logger,
        IDataPipelineIngressStore ingressStore,
        IDevicePluginRuntimeContext? runtimeContext = null)
        : this(overflowPersistence: null, logger, ingressStore, runtimeContext)
    {
    }

    public DataPipelineService(
        IIngressOverflowPersistence? overflowPersistence,
        ILogService logger,
        IDataPipelineIngressStore? ingressStore,
        IDevicePluginRuntimeContext? runtimeContext = null)
    {
        _queue = Channel.CreateBounded<CellCompletedRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            // 仍保留 Wait，避免 DropWrite 静默丢数据；实际入队使用 TryWrite，队列满时立即走溢出持久化。
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _legacyOverflowPersistence = overflowPersistence;
        _ingressStore = ingressStore;
        _logger = logger;
        _runtimeContext = runtimeContext;
    }

    public int PendingCount => Volatile.Read(ref _pendingCount);

    public int OverflowCount => Volatile.Read(ref _overflowCount);

    public int SpillCount => Volatile.Read(ref _spillCount);

    public bool TryDequeue(out CellCompletedRecord? record)
    {
        if (_queue.Reader.TryRead(out var item))
        {
            Interlocked.Decrement(ref _pendingCount);
            record = item;
            return true;
        }

        record = null;
        return false;
    }

    public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
        => _queue.Reader.WaitToReadAsync(cancellationToken);

    public async ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
    {
        if (record is null)
        {
            _logger.Warn("[数据管道] 入队失败：记录为空。");
            return DataPipelineEnqueueResult.Rejected("null_record");
        }

        if (record.CellData is null)
        {
            _logger.Warn("[数据管道] 入队失败：CellData 为空。");
            return DataPipelineEnqueueResult.Rejected("null_cell_data");
        }

        var bindingFailure = ApplyV3Binding(record);
        if (bindingFailure is not null)
        {
            _logger.Warn($"[数据管道] 入队失败：v3 设备插件身份不完整，原因={bindingFailure}。");
            return DataPipelineEnqueueResult.Rejected(bindingFailure);
        }

        var missingContext = ResolveMissingPlcContext(record);
        if (missingContext.Length > 0)
        {
            _logger.Warn($"[数据管道] 入队失败：缺少 PLC 上下文，字段={string.Join(",", missingContext)}。");
            return DataPipelineEnqueueResult.Rejected("missing_plc_context");
        }

        var conflictingContext = ResolveConflictingPlcContext(record);
        if (conflictingContext.Length > 0)
        {
            _logger.Warn(
                $"[数据管道] 入队失败：PLC 上下文冲突，字段={string.Join(",", conflictingContext)}。");
            return DataPipelineEnqueueResult.Rejected("conflicting_plc_context");
        }

        record.PlcCode = record.ResolvePlcCode().Trim();
        record.DeviceName = record.ResolveDeviceName().Trim();
        record.NetworkDeviceId = record.ResolveNetworkDeviceId();
        record.IdempotencyKeyVersion = CloudIdempotencyKeyVersion.PlcStableV2;

        if (_ingressStore is not null)
        {
            return await AcceptDurableIngressAsync(record, cancellationToken).ConfigureAwait(false);
        }

        if (_queue.Writer.TryWrite(record))
        {
            Interlocked.Increment(ref _pendingCount);

            var cellData = record.CellData;
            var result = cellData.CellResult switch
            {
                true => "OK",
                false => "NG",
                null => "Unknown"
            };

            _logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"结果=MemoryAccepted，业务结果={result}，待处理={PendingCount}。");
            return DataPipelineEnqueueResult.Accepted();
        }

        Interlocked.Increment(ref _overflowCount);
        _logger.Warn(
            $"{DataPipelineLogContext.Format(record)}[数据管道] " +
            $"结果=MemoryQueueFull，待处理={PendingCount}，容量={QueueCapacity}。");

        var overflowResult = await (_legacyOverflowPersistence
                ?? throw new InvalidOperationException(
                    "非 durable 入口模式缺少 Host API 2.0.x 兼容溢出存储。"))
            .PersistOverflowAsync(record, cancellationToken)
            .ConfigureAwait(false);

        if (overflowResult.PersistedTargetCount > 0)
        {
            Interlocked.Increment(ref _spillCount);
        }

        return overflowResult;
    }

    private string? ApplyV3Binding(CellCompletedRecord record)
    {
        var runtime = _runtimeContext?.Current;
        if (runtime is null || !runtime.IsV3)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(record.ClientCode)
            && !string.Equals(
                record.ClientCode.Trim(),
                runtime.ClientCode,
                StringComparison.OrdinalIgnoreCase))
        {
            return "client_code_conflict";
        }

        if (!string.Equals(record.ModuleId?.Trim(), runtime.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            return "module_id_conflict";
        }

        if (!string.IsNullOrWhiteSpace(record.ProcessType)
            && !string.Equals(
                record.ProcessType.Trim(),
                runtime.ProcessType,
                StringComparison.OrdinalIgnoreCase))
        {
            return "process_type_conflict";
        }

        if (!IsRequiredToken(record.CompletionId, 256))
        {
            return "completion_id_missing";
        }

        if (!IsRequiredToken(record.TypeKey, 128))
        {
            return "type_key_missing";
        }

        record.ClientCode = runtime.ClientCode;
        record.CompletionId = record.CompletionId.Trim();
        record.TypeKey = record.TypeKey.Trim().ToLowerInvariant();
        // The signed runtime Binding is the only authority for the Cloud-created process
        // classification. Plugin code may still expose a legacy ModuleId-like ProcessType on
        // CellData for ABI compatibility, but v3 routing/persistence must never consume it.
        record.ProcessType = runtime.ProcessType;
        return null;
    }

    private static bool IsRequiredToken(string? value, int maximumLength)
        => !string.IsNullOrWhiteSpace(value)
           && value.Trim().Length <= maximumLength
           && !value.Any(char.IsControl);

    private async ValueTask<DataPipelineEnqueueResult> AcceptDurableIngressAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken)
    {
        DataPipelineIngressAcceptance acceptance;
        try
        {
            acceptance = await _ingressStore!
                .AcceptAsync(record, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"结果=Rejected，原因码=DurableIngressUnavailable，" +
                $"异常类型={ex.GetType().Name}。");
            return DataPipelineEnqueueResult.Rejected("durable_ingress_unavailable");
        }

        if (acceptance.AlreadyCompleted)
        {
            _logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                "结果=AlreadyCompleted，本次幂等接受且不重复消费。");
            return DataPipelineEnqueueResult.Accepted();
        }

        if (_queue.Writer.TryWrite(acceptance.Record))
        {
            Interlocked.Increment(ref _pendingCount);
            _logger.Info(
                $"{DataPipelineLogContext.Format(record)}[数据管道] " +
                $"结果=DurableIngressAccepted，已通知主队列，待处理={PendingCount}。");
            return DataPipelineEnqueueResult.Accepted();
        }

        Interlocked.Increment(ref _overflowCount);
        Interlocked.Increment(ref _spillCount);
        _logger.Warn(
            $"{DataPipelineLogContext.Format(record)}[数据管道] " +
            "结果=DurableIngressAccepted，内存通知队列已满，将由恢复器重放。");
        return DataPipelineEnqueueResult.OverflowPersisted(1, 0);
    }

    private static string[] ResolveMissingPlcContext(CellCompletedRecord record)
    {
        var missing = new List<string>(5);
        if (string.IsNullOrWhiteSpace(record.PlcCode))
        {
            missing.Add(nameof(CellCompletedRecord.PlcCode));
        }

        if (record.NetworkDeviceId is not > 0)
        {
            missing.Add(nameof(CellCompletedRecord.NetworkDeviceId));
        }

        if (string.IsNullOrWhiteSpace(record.DeviceName))
        {
            missing.Add(nameof(CellCompletedRecord.DeviceName));
        }

        if (string.IsNullOrWhiteSpace(record.ModuleId))
        {
            missing.Add(nameof(CellCompletedRecord.ModuleId));
        }

        if (string.IsNullOrWhiteSpace(record.TaskKey))
        {
            missing.Add(nameof(CellCompletedRecord.TaskKey));
        }

        return missing.ToArray();
    }

    private static string[] ResolveConflictingPlcContext(CellCompletedRecord record)
    {
        var conflicts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(record.PlcCode)
            && !string.IsNullOrWhiteSpace(record.CellData.DeviceCode)
            && !string.Equals(
                record.PlcCode.Trim(),
                record.CellData.DeviceCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            conflicts.Add(nameof(CellCompletedRecord.PlcCode));
        }

        if (record.NetworkDeviceId is > 0
            && record.CellData.PlcDeviceId is > 0
            && record.NetworkDeviceId != record.CellData.PlcDeviceId)
        {
            conflicts.Add(nameof(CellCompletedRecord.NetworkDeviceId));
        }

        return conflicts.ToArray();
    }
}
