using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Events;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
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
    public IIoT.Edge.Module.Contracts.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Module.Contracts.DataPipeline.ConsumerFailureMode.BestEffort;
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
