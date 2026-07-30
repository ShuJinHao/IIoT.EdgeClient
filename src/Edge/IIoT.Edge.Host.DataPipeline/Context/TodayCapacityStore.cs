using IIoT.Edge.Module.Contracts.DataPipeline.Capacity;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Host.DataPipeline.Context;

/// <summary>
/// 当天产能内存存储实现
/// 
/// 通过 ProductionContextStore 按稳定 PlcCode 拿到对应 Context
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

    public string Increment(string plcCode, DateTime completedTime, bool isOk)
    {
        var ctx = GetRequiredContext(plcCode);
        return ctx.TodayCapacity.Increment(
            completedTime, isOk,
            _shiftConfig.DayStartTime,
            _shiftConfig.DayEndTime);
    }

    public TodayCapacity GetSnapshot(string plcCode)
    {
        var ctx = GetRequiredContext(plcCode);
        return ctx.TodayCapacity.CreateSnapshot();
    }

    public void Reset(string plcCode)
    {
        var ctx = GetRequiredContext(plcCode);
        ctx.TodayCapacity.Reset();
    }

    private ProductionContext GetRequiredContext(string plcCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plcCode);
        var matches = _contextStore.GetAll()
            .Where(context =>
                string.Equals(context.PlcCode, plcCode, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"未找到 PlcCode={plcCode} 的生产上下文，已拒绝按 DeviceName 创建。"),
            _ => throw new InvalidOperationException(
                $"PlcCode={plcCode} 匹配到多个生产上下文，已失败关闭。")
        };
    }
}
