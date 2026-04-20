using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using System.Threading.Channels;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

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
            _logger.Warn("[DataPipeline] Enqueue failed: record is null.");
            return DataPipelineEnqueueResult.Rejected("null_record");
        }

        if (record.CellData is null)
        {
            _logger.Warn("[DataPipeline] Enqueue failed: CellData is null.");
            return DataPipelineEnqueueResult.Rejected("null_cell_data");
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
                $"[{cellData.DeviceCode}] {cellData.ProcessType} queued. Result:{result}, Pending:{PendingCount}");
            return DataPipelineEnqueueResult.Accepted();
        }

        Interlocked.Increment(ref _overflowCount);
        _logger.Warn(
            $"[DataPipeline] Queue overflow for {record.CellData.ProcessType}. Pending:{PendingCount}, Capacity:{QueueCapacity}");

        var overflowResult = await _overflowPersistence
            .PersistOverflowAsync(record, cancellationToken)
            .ConfigureAwait(false);

        if (overflowResult.PersistedTargetCount > 0)
        {
            Interlocked.Increment(ref _spillCount);
        }

        return overflowResult;
    }
}
