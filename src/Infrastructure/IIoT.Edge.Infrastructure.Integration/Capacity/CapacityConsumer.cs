using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Events;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using MediatR;

namespace IIoT.Edge.Infrastructure.Integration.Capacity;

public class CapacityConsumer : ICapacityConsumer
{
    private readonly ITodayCapacityStore _todayCapacityStore;
    private readonly IDeviceService _deviceService;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ICapacityBufferStore _capacityBufferStore;
    private readonly IPublisher _publisher;
    private readonly ILogService _logger;
    private readonly IProductionTimeProvider _productionTime;

    public string Name => "Capacity";
    public int Order => 10;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.None;

    public CapacityConsumer(
        ITodayCapacityStore todayCapacityStore,
        IDeviceService deviceService,
        ILocalSystemRuntimeConfigService runtimeConfig,
        ICapacityBufferStore capacityBufferStore,
        IPublisher publisher,
        ILogService logger,
        IProductionTimeProvider productionTime)
    {
        _todayCapacityStore = todayCapacityStore;
        _deviceService = deviceService;
        _runtimeConfig = runtimeConfig;
        _capacityBufferStore = capacityBufferStore;
        _publisher = publisher;
        _logger = logger;
        _productionTime = productionTime;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var cellData = record.CellData;
            var deviceName = cellData.DeviceName;
            var completedTime = _productionTime.ToBusinessTime(cellData.CompletedTime ?? _productionTime.UtcNow);
            var isOk = cellData.CellResult ?? false;

            var shiftCode = _todayCapacityStore.Increment(deviceName, completedTime, isOk);
            var snapshot = _todayCapacityStore.GetSnapshot(deviceName);

            await _publisher.Publish(new CapacityUpdatedNotification
            {
                Snapshot = snapshot
            }, cancellationToken);

            if (_runtimeConfig.Current.SystemCloudEnabled && !_deviceService.CanUploadToCloud)
            {
                await _capacityBufferStore.SaveAsync(new CapacityRecord
                {
                    Barcode = cellData.DisplayLabel,
                    CellResult = isOk,
                    ShiftCode = shiftCode,
                    CompletedTime = completedTime,
                    CreatedAt = _productionTime.UtcNow,
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
