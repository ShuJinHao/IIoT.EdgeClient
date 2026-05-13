using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

/// <summary>
/// 当天产能内存存储接口。
/// 
/// 通过 deviceName 定位对应 ProductionContext 的 TodayCapacity。
/// 
/// 写入方：CapacityConsumer，每个电芯完成时调用 Increment。
/// 读取方：CapacitySyncTask 读取快照并提交到云端，界面层读取后实时显示。
/// </summary>
public interface ITodayCapacityStore
{
    /// <summary>
    /// 产能计数加 1，并在内部自动判定班次和跨天清零逻辑。
    /// </summary>
    /// <param name="deviceName">设备名，作为聚合键，例如“PLC-1”。</param>
    /// <returns>本次记录归属的班次编码："D" 表示白班，"N" 表示夜班。</returns>
    string Increment(string deviceName, DateTime completedTime, bool isOk);

    /// <summary>
    /// 获取指定设备当天的产能快照。
    /// </summary>
    TodayCapacity GetSnapshot(string deviceName);

    /// <summary>
    /// 手动清零指定设备的当天产能。
    /// </summary>
    void Reset(string deviceName);
}
