using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.Application.Abstractions.Device;
using System.Text.Json;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能云端查询服务。
/// 负责封装产能相关的云端 HTTP 调用与响应解析。
/// <c>deviceId</c> 表示云端为设备分配的唯一标识，<c>plcName</c> 用于区分同一上位机下的不同 PLC。
/// </summary>
public class CapacityCloudQueryService
{
    private readonly ICloudHttpClient _cloudHttpClient;
    private readonly ICloudApiPathProvider _apiPathProvider;
    private readonly ShiftConfig _shiftConfig;

    public CapacityCloudQueryService(
        ICloudHttpClient cloudHttpClient,
        ICloudApiPathProvider apiPathProvider,
        ShiftConfig shiftConfig)
    {
        _cloudHttpClient = cloudHttpClient;
        _apiPathProvider = apiPathProvider;
        _shiftConfig = shiftConfig;
    }

    // 按生产日查询：优先使用分时明细，缺失时回退到汇总数据。

    public async Task<List<DailyCapacityVm>> QueryByProductionDayAsync(
        Guid deviceId, DateTime productionDate, string plcName)

    {
        var nextDay = productionDate.AddDays(1);

        var hourlyToday = await QueryHourlyAsync(deviceId, productionDate, plcName);
        var hourlyNextDay = await QueryHourlyAsync(deviceId, nextDay, plcName);

        var nightSlots = hourlyNextDay
            .Where(x => x.StartHour < _shiftConfig.DayStartTime.Hours ||
                        (x.StartHour == _shiftConfig.DayStartTime.Hours &&
                         x.StartMinute < _shiftConfig.DayStartTime.Minutes))
            .ToList();

        if (hourlyToday.Count > 0 || nightSlots.Count > 0)
        {
            return hourlyToday.Concat(nightSlots)
                .OrderBy(x => x.SlotOrder)
                .Select(x => new DailyCapacityVm
                {
                    Date = productionDate.ToString("MM-dd"),
                    DateFull = x.TimeLabel,
                    DayOfWeek = x.ShiftCode,
                    Total = x.TotalCount,
                    OkCount = x.OkCount,
                    NgCount = x.NgCount,
                    Yield = x.TotalCount > 0
                        ? $"{x.OkCount * 100.0 / x.TotalCount:F1}%"
                        : "0%"
                }).ToList();
        }

        var summaryToday = await QuerySummaryAsync(deviceId, productionDate, plcName);
        var summaryNextDay = await QuerySummaryAsync(deviceId, nextDay, plcName);

        if (summaryToday is null && summaryNextDay is null)
            return new List<DailyCapacityVm>();

        var totalCount = (summaryToday?.TotalCount ?? 0) + (summaryNextDay?.NightShiftTotal ?? 0);
        var okCount = (summaryToday?.OkCount ?? 0) + (summaryNextDay?.NightShiftOk ?? 0);
        var ngCount = (summaryToday?.NgCount ?? 0) + (summaryNextDay?.NightShiftNg ?? 0);

        return new List<DailyCapacityVm>
        {
            new()
            {
                Date            = productionDate.ToString("MM-dd"),
                DateFull        = productionDate.ToString("yyyy-MM-dd"),
                DayOfWeek       = productionDate.ToString("ddd"),
                Total           = totalCount,
                OkCount         = okCount,
                NgCount         = ngCount,
                Yield           = totalCount > 0 ? $"{okCount * 100.0 / totalCount:F1}%" : "0%",
                DayShiftTotal   = summaryToday?.DayShiftTotal  ?? 0,
                DayShiftOk      = summaryToday?.DayShiftOk     ?? 0,
                DayShiftNg      = summaryToday?.DayShiftNg     ?? 0,
                NightShiftTotal = (summaryToday?.NightShiftTotal ?? 0) + (summaryNextDay?.NightShiftTotal ?? 0),
                NightShiftOk    = (summaryToday?.NightShiftOk   ?? 0) + (summaryNextDay?.NightShiftOk   ?? 0),
                NightShiftNg    = (summaryToday?.NightShiftNg   ?? 0) + (summaryNextDay?.NightShiftNg   ?? 0),
            }
        };
    }

    // 按月查询：返回当月每日汇总。

    public async Task<List<DailyCapacityVm>> QueryByMonthAsync(
        Guid deviceId, int year, int month, string plcName)

    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var rows = await QuerySummaryRangeAsync(deviceId, startDate, endDate, plcName);
        return rows.Where(r => r.Total > 0).ToList();
    }

    // 按年查询：按月份聚合全年汇总。

    public async Task<List<DailyCapacityVm>> QueryByYearAsync(
        Guid deviceId, int year, string plcName)

    {
        var startDate = new DateTime(year, 1, 1);
        var endDate = new DateTime(year, 12, 31);

        var rows = await QuerySummaryRangeAsync(deviceId, startDate, endDate, plcName);

        return rows
            .GroupBy(r => r.DateFull.Substring(0, 7))
            .Select(g =>
            {
                var total = g.Sum(x => x.Total);
                var ok = g.Sum(x => x.OkCount);
                var ng = g.Sum(x => x.NgCount);
                return new DailyCapacityVm
                {
                    Date = g.Key,
                    DateFull = g.Key,
                    DayOfWeek = "--",
                    Total = total,
                    OkCount = ok,
                    NgCount = ng,
                    Yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0%"
                };
            })
            .OrderBy(r => r.DateFull)
            .ToList();
    }

    // 私有查询：调用云端接口并解析响应。

    private async Task<List<HourlySlotVm>> QueryHourlyAsync(
        Guid deviceId, DateTime date, string plcName)

    {
        var path = _apiPathProvider.GetCapacityHourlyPath();
        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";


        var result = await _cloudHttpClient.GetAsync(url);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Payload)) return new();

        try
        {
            using var doc = JsonDocument.Parse(result.Payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return new();

            var slots = new List<HourlySlotVm>();
            foreach (var item in root.EnumerateArray())
            {
                var hour = ReadInt(item, "hour", "Hour");
                var minute = ReadInt(item, "minute", "Minute");
                var total = ReadInt(item, "totalCount", "TotalCount");
                var ok = ReadInt(item, "okCount", "OkCount");
                var ng = ReadInt(item, "ngCount", "NgCount");
                var shift = ReadString(item, "shiftCode", "ShiftCode");
                var label = ReadString(item, "timeLabel", "TimeLabel");

                if (string.IsNullOrWhiteSpace(label))
                {
                    var endMinute = minute == 30 ? 0 : 30;
                    var endHour = minute == 30 ? (hour + 1) % 24 : hour;
                    label = $"{hour:D2}:{minute:D2}-{endHour:D2}:{endMinute:D2}";
                }

                if (string.IsNullOrWhiteSpace(shift))
                    shift = GetShiftCode(hour, minute);

                slots.Add(new HourlySlotVm
                {
                    SlotOrder = hour * 2 + (minute >= 30 ? 1 : 0),
                    Hour = hour,
                    Minute = minute,
                    StartHour = hour,
                    StartMinute = minute,
                    TimeLabel = label,
                    ShiftCode = shift,
                    TotalCount = total,
                    OkCount = ok,
                    NgCount = ng
                });
            }
            return slots.OrderBy(x => x.SlotOrder).ToList();
        }
        catch { return new(); }
    }

    private async Task<DailySummaryVm?> QuerySummaryAsync(
        Guid deviceId, DateTime date, string plcName)

    {
        var path = _apiPathProvider.GetCapacitySummaryPath();
        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";


        var result = await _cloudHttpClient.GetAsync(url);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Payload)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result.Payload);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0) return null;
                root = root[0];
            }

            var total = ReadInt(root, "totalCount", "TotalCount");
            var ok = ReadInt(root, "okCount", "OkCount");
            var ng = ReadInt(root, "ngCount", "NgCount");
            var dayTotal = ReadInt(root, "dayShiftTotal", "DayShiftTotal");
            var dayOk = ReadInt(root, "dayShiftOk", "DayShiftOk");
            var dayNg = ReadInt(root, "dayShiftNg", "DayShiftNg");
            var nightTotal = ReadInt(root, "nightShiftTotal", "NightShiftTotal");
            var nightOk = ReadInt(root, "nightShiftOk", "NightShiftOk");
            var nightNg = ReadInt(root, "nightShiftNg", "NightShiftNg");

            if (total == 0) total = dayTotal + nightTotal;
            if (ok == 0 && ng == 0) { ok = dayOk + nightOk; ng = dayNg + nightNg; }

            return new DailySummaryVm
            {
                TotalCount = total,
                OkCount = ok,
                NgCount = ng,
                DayShiftTotal = dayTotal,
                DayShiftOk = dayOk,
                DayShiftNg = dayNg,
                NightShiftTotal = nightTotal,
                NightShiftOk = nightOk,
                NightShiftNg = nightNg
            };
        }
        catch { return null; }
    }

    private async Task<List<DailyCapacityVm>> QuerySummaryRangeAsync(
        Guid deviceId, DateTime startDate, DateTime endDate, string plcName)

    {
        var path = _apiPathProvider.GetCapacitySummaryRangePath();
        var url = string.IsNullOrEmpty(plcName)
            ? $"{path}?deviceId={deviceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}"
            : $"{path}?deviceId={deviceId}&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}";


        var result = await _cloudHttpClient.GetAsync(url);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Payload)) return new();

        try
        {
            using var doc = JsonDocument.Parse(result.Payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return new();

            var rows = new List<DailyCapacityVm>();
            foreach (var item in root.EnumerateArray())
            {
                var dateStr = ReadString(item, "date", "Date");
                var total = ReadInt(item, "totalCount", "TotalCount");
                var ok = ReadInt(item, "okCount", "OkCount");
                var ng = ReadInt(item, "ngCount", "NgCount");
                var dayTotal = ReadInt(item, "dayShiftTotal", "DayShiftTotal");
                var dayOk = ReadInt(item, "dayShiftOk", "DayShiftOk");
                var dayNg = ReadInt(item, "dayShiftNg", "DayShiftNg");
                var nightTotal = ReadInt(item, "nightShiftTotal", "NightShiftTotal");
                var nightOk = ReadInt(item, "nightShiftOk", "NightShiftOk");
                var nightNg = ReadInt(item, "nightShiftNg", "NightShiftNg");

                if (string.IsNullOrWhiteSpace(dateStr)) continue;

                rows.Add(new DailyCapacityVm
                {
                    Date = dateStr.Length >= 10 ? dateStr.Substring(5, 5) : dateStr,
                    DateFull = dateStr,
                    DayOfWeek = DateTime.TryParse(dateStr, out var dt) ? dt.ToString("ddd") : "--",
                    Total = total,
                    OkCount = ok,
                    NgCount = ng,
                    Yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0%",
                    DayShiftTotal = dayTotal,
                    DayShiftOk = dayOk,
                    DayShiftNg = dayNg,
                    NightShiftTotal = nightTotal,
                    NightShiftOk = nightOk,
                    NightShiftNg = nightNg
                });
            }
            return rows;
        }
        catch { return new(); }
    }

    // 辅助方法。

    public DateTime GetProductionDate(DateTime now)
        => now.TimeOfDay < _shiftConfig.DayStartTime
            ? now.Date.AddDays(-1)
            : now.Date;

    private string GetShiftCode(int hour, int minute)
    {
        var t = new TimeSpan(hour, minute, 0);
        var isDay = t >= _shiftConfig.DayStartTime && t < _shiftConfig.DayEndTime;
        return isDay ? "D" : "N";
    }

    private static int ReadInt(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n)) return n;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var s)) return s;
        }
        return 0;
    }

    private static string ReadString(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? "";
        }
        return "";
    }
}
