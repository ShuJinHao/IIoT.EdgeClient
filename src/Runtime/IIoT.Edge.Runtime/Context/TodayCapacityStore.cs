using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

namespace IIoT.Edge.Runtime.Context;

/// <summary>
/// 当天产能内存存储实现
/// 
/// 通过 ProductionContextStore 按 deviceName 拿到对应 Context
/// 操作 Context.TodayCapacity 内存对象
/// 持久化跟随 ProductionContextStore 的 JSON 自动走
/// 
/// 班次分界点从 ShiftConfig（appsettings.json）读取
/// </summary>
public class TodayCapacityStore : ITodayCapacityStore
{
    private readonly IProductionContextStore _contextStore;
    private readonly ShiftConfig _shiftConfig;

    public TodayCapacityStore(
        IProductionContextStore contextStore,
        ShiftConfig shiftConfig)
    {
        _contextStore = contextStore;
        _shiftConfig = shiftConfig;
    }

    public string Increment(string deviceName, DateTime completedTime, bool isOk)
    {
        var ctx = _contextStore.GetOrCreate(deviceName);
        return ctx.TodayCapacity.Increment(
            completedTime, isOk,
            _shiftConfig.DayStartTime,
            _shiftConfig.DayEndTime);
    }

    public TodayCapacity GetSnapshot(string deviceName)
    {
        var ctx = _contextStore.GetOrCreate(deviceName);
        return ctx.TodayCapacity.CreateSnapshot();
    }

    public void Reset(string deviceName)
    {
        var ctx = _contextStore.GetOrCreate(deviceName);
        ctx.TodayCapacity.Reset();
    }
}
