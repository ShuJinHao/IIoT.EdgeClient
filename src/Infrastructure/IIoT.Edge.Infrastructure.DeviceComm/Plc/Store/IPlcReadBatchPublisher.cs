namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

/// <summary>
/// Host 内部的 PLC 整批读取发布能力。扫描任务必须通过此接口一次提交完整轮次，
/// 不得逐信号降级发布。
/// </summary>
internal interface IPlcReadBatchPublisher
{
    void PublishReadBatch(IReadOnlyDictionary<string, PlcReadSignalUpdate> signalUpdates);
}
