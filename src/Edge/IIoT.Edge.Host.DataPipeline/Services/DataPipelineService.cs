using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using System.Threading.Channels;

namespace IIoT.Edge.Host.DataPipeline.Services;

public class DataPipelineService : IDataPipelineService
{
    private const int QueueCapacity = 5000;

    private readonly Channel<CellCompletedRecord> _queue;
    private readonly IIngressOverflowPersistence _overflowPersistence;
    private readonly ILogService _logger;
    private int _pendingCount;
    private int _overflowCount;
    private int _spillCount;

    public DataPipelineService(
        IIngressOverflowPersistence overflowPersistence,
        ILogService logger)
    {
        _queue = Channel.CreateBounded<CellCompletedRecord>(new BoundedChannelOptions(QueueCapacity)
        {
            // 仍保留 Wait，避免 DropWrite 静默丢数据；实际入队使用 TryWrite，队列满时立即走溢出持久化。
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _overflowPersistence = overflowPersistence;
        _logger = logger;
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

        var missingContext = ResolveMissingPlcContext(record);
        if (missingContext.Length > 0)
        {
            _logger.Warn($"[数据管道] 入队失败：缺少 PLC 上下文，字段={string.Join(",", missingContext)}。");
            return DataPipelineEnqueueResult.Rejected("missing_plc_context");
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

            var deviceName = record.ResolveDeviceName();
            _logger.Info(
                $"[PLC-{deviceName}][数据管道] 工序={cellData.ProcessType} 已入队，结果={result}，待处理={PendingCount}。");
            return DataPipelineEnqueueResult.Accepted();
        }

        Interlocked.Increment(ref _overflowCount);
        _logger.Warn(
            $"[数据管道] 队列已满，准备写入溢出补偿。工序={record.CellData.ProcessType}，待处理={PendingCount}，容量={QueueCapacity}");

        var overflowResult = await _overflowPersistence
            .PersistOverflowAsync(record, cancellationToken)
            .ConfigureAwait(false);

        if (overflowResult.PersistedTargetCount > 0)
        {
            Interlocked.Increment(ref _spillCount);
        }

        return overflowResult;
    }

    private static string[] ResolveMissingPlcContext(CellCompletedRecord record)
    {
        var missing = new List<string>(4);
        if (record.ResolveNetworkDeviceId() is not > 0)
        {
            missing.Add(nameof(CellCompletedRecord.NetworkDeviceId));
        }

        if (string.IsNullOrWhiteSpace(record.ResolveDeviceName()))
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
}
