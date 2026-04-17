using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>
/// 历史数据生成场景
///
/// 生成 2026-01-01 到 2026-04-02 的虚拟电芯记录
/// 批量写入 SQLite capacity_buffer（事务批量插入，速度快）
/// 写入后切换到真实 WPF 项目，触发离线补传链路验证
///
/// 时间归属严格按生产日逻辑：00:00-08:30 归前一自然日
/// </summary>
public class HistoricalDataScenario : ITestScenario
{
    public string Name => "历史数据生成（2026-01 至 2026-04-02）";

    private readonly ICapacityBufferStore _bufferStore;
    private readonly ShiftConfig _shiftConfig;
    private readonly ILogService _logger;

    private static readonly DateTime StartDate = new(2026, 1, 1);
    private static readonly DateTime EndDate = new(2026, 4, 2);

    // 固定种子，保证每次生成数据一致
    private readonly Random _rng = new(42);

    private const int BatchSize = 5000;

    // 模拟两台 PLC 设备
    private static readonly string[] PlcNames = { "test", "test1" };


    public HistoricalDataScenario(
        ICapacityBufferStore bufferStore,
        ShiftConfig shiftConfig,
        ILogService logger)
    {
        _bufferStore = bufferStore;
        _shiftConfig = shiftConfig;
        _logger = logger;
    }

    public async Task<ScenarioResult> RunAsync(CancellationToken ct = default)
    {
        var result = new ScenarioResult { Name = Name, Passed = false };

        _logger.Info($"[历史数据] 开始生成：{StartDate:yyyy-MM-dd} ~ {EndDate:yyyy-MM-dd}");

        var batch = new List<CapacityRecord>(BatchSize);
        int totalRows = 0;
        int dayCount = 0;

        var current = StartDate;
        while (current <= EndDate && !ct.IsCancellationRequested)
        {
            // 每天为两台 PLC 分别生成记录
            foreach (var plcName in PlcNames)
                GenerateDayRecords(current, batch, plcName);


            dayCount++;
            current = current.AddDays(1);

            if (batch.Count >= BatchSize || dayCount % 7 == 0)
            {
                await _bufferStore.SaveBatchAsync(batch);
                totalRows += batch.Count;
                _logger.Info($"[历史数据] 进度：{dayCount} 天，已写入 {totalRows} 条");
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _bufferStore.SaveBatchAsync(batch);
            totalRows += batch.Count;
        }

        var bufferCount = await _bufferStore.GetCountAsync();
        var msg = $"完成：{dayCount} 天，共写入 {totalRows} 条电芯记录，SQLite 当前缓冲 {bufferCount} 条";
        _logger.Info($"[历史数据] {msg}");
        _logger.Info("[历史数据] 请切换到真实 WPF 项目，上线后将自动触发离线补传");

        result.Passed = totalRows > 0;
        result.Assertions.Add(new AssertionResult
        {
            Description = "历史数据写入 SQLite",
            Passed = totalRows > 0,
            Expected = "写入记录数 > 0",
            Actual = $"{totalRows} 条"
        });
        result.Assertions.Add(new AssertionResult
        {
            Description = "SQLite 缓冲确认",
            Passed = bufferCount > 0,
            Expected = "缓冲区有数据",
            Actual = $"{bufferCount} 条"
        });

        return result;
    }

    /// <summary>生成单日单台 PLC 的电芯记录并追加到批处理列表。</summary>
    private void GenerateDayRecords(DateTime naturalDay, List<CapacityRecord> batch, string plcName)

    {
        for (int slotIndex = 0; slotIndex < 48; slotIndex++)
        {
            var startHour = slotIndex / 2;
            var startMinute = slotIndex % 2 == 0 ? 0 : 30;
            var slotTime = naturalDay.AddHours(startHour).AddMinutes(startMinute);

            var productionDate = slotTime.TimeOfDay < _shiftConfig.DayStartTime
                ? naturalDay.AddDays(-1)
                : naturalDay;

            var isDayShift = slotTime.TimeOfDay >= _shiftConfig.DayStartTime
                          && slotTime.TimeOfDay < _shiftConfig.DayEndTime;
            var shiftCode = isDayShift ? "D" : "N";

            var isDeepNight = startHour >= 22 || startHour < 6;
            if (isDeepNight && _rng.NextDouble() < 0.30) continue;

            var baseCount = isDayShift
                ? _rng.Next(180, 280)
                : _rng.Next(120, 200);

            if (naturalDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                baseCount = (int)(baseCount * 0.75);

            var ngRate = _rng.NextDouble() < 0.05
                ? _rng.NextDouble() * 0.08 + 0.03
                : _rng.NextDouble() * 0.025 + 0.005;

            var ngCount = (int)(baseCount * ngRate);
            var okCount = baseCount - ngCount;

            for (int i = 0; i < baseCount; i++)
            {
                var offsetSeconds = _rng.Next(0, 1800);
                var completedTime = slotTime.AddSeconds(offsetSeconds);

                batch.Add(new CapacityRecord
                {
                    Barcode = $"{plcName}-{productionDate:yyyyMMdd}{startHour:D2}{startMinute:D2}{i:D4}",

                    CellResult = i < okCount,
                    ShiftCode = shiftCode,
                    CompletedTime = completedTime,
                    CreatedAt = DateTime.Now,
                    PlcName = plcName

                });
            }
        }
    }
}
