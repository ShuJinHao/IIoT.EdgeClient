using IIoT.Edge.Application.Common.DataPipeline;
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
        ArgumentNullException.ThrowIfNull(deviceService);
        ArgumentNullException.ThrowIfNull(runtimeConfig);
        ArgumentNullException.ThrowIfNull(capacityBufferStore);
        _publisher = publisher;
        _logger = logger;
        _productionTime = productionTime;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var cellData = record.CellData;
            var plcCode = record.ResolvePlcCode();
            var completedTime = _productionTime.ToBusinessTime(cellData.CompletedTime ?? _productionTime.UtcNow);
            var isOk = cellData.CellResult ?? false;

            var shiftCode = _todayCapacityStore.Increment(plcCode, completedTime, isOk);
            var snapshot = _todayCapacityStore.GetSnapshot(plcCode);

            await _publisher.Publish(new CapacityUpdatedNotification
            {
                Snapshot = snapshot
            }, cancellationToken);

            // 容量卡片只维护当前运行内存投影。完成事实的 Cloud 失败补传由
            // DataPipeline 的 Cloud retry/fallback/deadletter 通道统一承担，不再另写长期 CapacityBuffer。

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"[CorrelationId={DataPipelineCompletionIdentity.Create(record)}]" +
                $"[PlcCode={record.ResolvePlcCode()}][TaskKey={record.TaskKey}]" +
                $"[产能] 业务标识={record.CellData.DisplayLabel}，结果=Failed，" +
                $"原因码=CapacityConsumerFailed，异常类型={ex.GetType().Name}。");
            return false;
        }
    }
}
