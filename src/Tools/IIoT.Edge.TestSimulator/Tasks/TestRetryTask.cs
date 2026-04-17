using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Runtime.DataPipeline.Tasks;

namespace IIoT.Edge.TestSimulator.Tasks;

/// <summary>
/// 模拟场景可直接触发的重试任务
/// 用于测试时立即执行一轮重试流程
/// </summary>
public sealed class TestRetryTask : RetryTask
{
    public TestRetryTask(
        ILogService                 logger,
        IFailedRecordStore          failedStore,
        IDeviceService              deviceService,
        IEnumerable<ICellDataConsumer> consumers,
        IDeviceLogSyncTask?         deviceLogSync = null,
        ICapacitySyncTask?          capacitySync  = null,
        ICloudBatchConsumer?        cloudBatchConsumer = null)
        : base("Cloud", logger, failedStore, deviceService, consumers, deviceLogSync, capacitySync, cloudBatchConsumer)
    {
    }

    /// <summary>立即执行一轮重试流程（仅用于模拟场景）</summary>
    public Task TriggerAsync() => ExecuteAsync();
}
