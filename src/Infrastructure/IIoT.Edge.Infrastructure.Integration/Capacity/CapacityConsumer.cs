using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Events;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using MediatR;

namespace IIoT.Edge.Infrastructure.Integration.Capacity;

public class CapacityConsumer : ICapacityConsumer
{
    private readonly ITodayCapacityStore _todayCapacityStore;
    private readonly IDeviceService _deviceService;
    private readonly ICapacityBufferStore _capacityBufferStore;
    private readonly IPublisher _publisher;
    private readonly ILogService _logger;

    public string Name => "Capacity";
    public int Order => 10;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public string? RetryChannel => null;

    public CapacityConsumer(
        ITodayCapacityStore todayCapacityStore,
        IDeviceService deviceService,
        ICapacityBufferStore capacityBufferStore,
        IPublisher publisher,
        ILogService logger)
    {
        _todayCapacityStore = todayCapacityStore;
        _deviceService = deviceService;
        _capacityBufferStore = capacityBufferStore;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        try
        {
            var cellData = record.CellData;
            var deviceName = cellData.DeviceName;
            var completedTime = cellData.CompletedTime ?? DateTime.UtcNow;
            var isOk = cellData.CellResult ?? false;

            var shiftCode = _todayCapacityStore.Increment(deviceName, completedTime, isOk);
            var snapshot = _todayCapacityStore.GetSnapshot(deviceName);

            await _publisher.Publish(new CapacityUpdatedNotification
            {
                Snapshot = snapshot
            });

            if (!_deviceService.CanUploadToCloud)
            {
                await _capacityBufferStore.SaveAsync(new CapacityRecord
                {
                    Barcode = cellData.DisplayLabel,
                    CellResult = isOk,
                    ShiftCode = shiftCode,
                    CompletedTime = completedTime,
                    CreatedAt = DateTime.UtcNow,
                    PlcName = deviceName
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[Capacity] 产能统计异常: {ex.Message}");
            return true;
        }
    }
}
