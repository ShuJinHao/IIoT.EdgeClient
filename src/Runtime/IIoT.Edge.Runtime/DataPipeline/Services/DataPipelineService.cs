using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using System.Collections.Concurrent;

namespace IIoT.Edge.Runtime.DataPipeline.Services;

public class DataPipelineService : IDataPipelineService
{
    private readonly ConcurrentQueue<CellCompletedRecord> _queue = new();
    private readonly ILogService _logger;

    public DataPipelineService(ILogService logger)
    {
        _logger = logger;
    }

    public int PendingCount => _queue.Count;

    public bool TryDequeue(out CellCompletedRecord? record)
    {
        var result = _queue.TryDequeue(out var item);
        record = item;
        return result;
    }

    public void Enqueue(CellCompletedRecord record)
    {
        if (record is null)
        {
            _logger.Warn("[DataPipeline] Enqueue failed: record is null.");
            return;
        }

        if (record.CellData is null)
        {
            _logger.Warn("[DataPipeline] Enqueue failed: CellData is null.");
            return;
        }

        _queue.Enqueue(record);

        var cellData = record.CellData;
        var result = cellData.CellResult switch
        {
            true => "OK",
            false => "NG",
            null => "Unknown"
        };

        _logger.Info(
            $"[{cellData.DeviceCode}] {cellData.ProcessType} queued. Result:{result}, Pending:{_queue.Count}");
    }
}
