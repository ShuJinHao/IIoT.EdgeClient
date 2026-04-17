using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.TestSimulator.Fakes;

namespace IIoT.Edge.TestSimulator.Consumers;

/// <summary>
/// 测试用产能消费者：产能计数 + 离线时写 SQLite 缓冲，不依赖 MediatR
/// 始终返回 true，不进重传队列
/// </summary>
public sealed class SimCapacityConsumer : ICapacityConsumer
{
    private readonly FakeTodayCapacityStore _capacityStore;
    private readonly IDeviceService _deviceService;
    private readonly ICapacityBufferStore _bufferStore;
    private readonly ILogService _logger;

    public string Name => "Capacity";
    public int Order => 10;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public string? RetryChannel => null;

    public SimCapacityConsumer(
        FakeTodayCapacityStore capacityStore,
        IDeviceService deviceService,
        ICapacityBufferStore bufferStore,
        ILogService logger)
    {
        _capacityStore = capacityStore;
        _deviceService = deviceService;
        _bufferStore = bufferStore;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        var cellData = record.CellData;
        var completedTime = cellData.CompletedTime ?? DateTime.Now;
        var isOk = cellData.CellResult ?? false;

        var shiftCode = _capacityStore.Increment(cellData.DeviceName, completedTime, isOk);

        if (_deviceService.CurrentState == NetworkState.Offline)
        {
            await _bufferStore.SaveAsync(new CapacityRecord
            {
                Barcode = cellData.DisplayLabel,
                CellResult = isOk,
                ShiftCode = shiftCode,
                CompletedTime = completedTime,
                CreatedAt = DateTime.Now,
                PlcName = cellData.DeviceName

            });
            _logger.Info($"[SimCapacity] 离线缓存: {cellData.DisplayLabel}");
        }
        else
        {
            _logger.Info($"[SimCapacity] 产能计数: {cellData.DisplayLabel} 班次={shiftCode}");
        }

        return true;
    }
}
