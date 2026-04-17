using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

namespace IIoT.Edge.TestSimulator.Fakes;

/// <summary>
/// 纯内存产能存储，不依赖 ProductionContextStore 和配置文件
/// </summary>
public sealed class FakeTodayCapacityStore : ITodayCapacityStore
{
    private readonly TodayCapacity _capacity = new();

    // 白班 08:30 ~ 20:30（与生产默认值一致）
    private static readonly TimeSpan DayStart = new(8, 30, 0);
    private static readonly TimeSpan DayEnd   = new(20, 30, 0);

    public string Increment(string deviceName, DateTime completedTime, bool isOk)
        => _capacity.Increment(completedTime, isOk, DayStart, DayEnd);

    public TodayCapacity GetSnapshot(string deviceName) => _capacity;

    public void Reset(string deviceName) => _capacity.Reset();

    /// <summary>清空所有计数（跨场景重置用）</summary>
    public void ResetAll() => _capacity.Reset();
}

