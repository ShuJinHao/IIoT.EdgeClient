using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Features.Production.CapacityView;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// 产能查询视觉验收数据源，只服务 UI 截图验收，不调用云端产能接口。
/// </summary>
public sealed class VisualTestCapacityQueryFacade : ICapacityQueryFacade
{
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged
    {
        add { }
        remove { }
    }

    public bool IsOnline => true;

    public Task<CapacityViewResult> LoadTodayAsync(
        string plcName,
        CancellationToken cancellationToken = default)
        => Task.FromResult(BuildResult(DateTime.Today, CapacityQueryModes.Day, plcName));

    public Task<CapacityViewResult> QueryHistoryAsync(
        string queryMode,
        DateTime queryDate,
        string plcName,
        CancellationToken cancellationToken = default)
        => Task.FromResult(BuildResult(queryDate, queryMode, plcName));

    private static CapacityViewResult BuildResult(DateTime queryDate, string queryMode, string plcName)
    {
        var rows = queryMode switch
        {
            CapacityQueryModes.Month => BuildMonthRows(queryDate),
            CapacityQueryModes.Year => BuildYearRows(queryDate),
            _ => BuildDayRows(queryDate)
        };

        return ToResult(rows, queryMode == CapacityQueryModes.Year ? 12 : Math.Max(1, rows.Count));
    }

    private static List<DailyCapacitySnapshot> BuildDayRows(DateTime queryDate)
    {
        var rows = new List<DailyCapacitySnapshot>();
        var baseTotal = 520;

        for (var index = 0; index < 16; index++)
        {
            var time = queryDate.Date.AddHours(8).AddMinutes(index * 45);
            var total = baseTotal + index * 17 + DateTimeOffset.Now.Minute % 11;
            var ng = index % 5 == 0 ? 5 : 2 + index % 4;
            rows.Add(CreateRow(
                time.ToString("MM-dd"),
                $"{time:HH:mm}-{time.AddMinutes(45):HH:mm}",
                index < 8 ? "白班" : "夜班",
                total,
                ng));
        }

        return rows;
    }

    private static List<DailyCapacitySnapshot> BuildMonthRows(DateTime queryDate)
    {
        var days = DateTime.DaysInMonth(queryDate.Year, queryDate.Month);
        var rows = new List<DailyCapacitySnapshot>();

        for (var day = 1; day <= Math.Min(days, 18); day++)
        {
            var date = new DateTime(queryDate.Year, queryDate.Month, day);
            var total = 11800 + day * 146;
            var ng = 28 + day % 8;
            rows.Add(CreateRow(
                date.ToString("MM-dd"),
                date.ToString("yyyy-MM-dd"),
                date.ToString("ddd"),
                total,
                ng));
        }

        return rows;
    }

    private static List<DailyCapacitySnapshot> BuildYearRows(DateTime queryDate)
    {
        var rows = new List<DailyCapacitySnapshot>();
        for (var month = 1; month <= 12; month++)
        {
            var date = new DateTime(queryDate.Year, month, 1);
            var total = 260000 + month * 5200;
            var ng = 680 + month * 11;
            rows.Add(CreateRow(
                date.ToString("yyyy-MM"),
                date.ToString("yyyy-MM"),
                "--",
                total,
                ng));
        }

        return rows;
    }

    private static DailyCapacitySnapshot CreateRow(
        string date,
        string dateFull,
        string dayOfWeek,
        int total,
        int ng)
    {
        var ok = Math.Max(0, total - ng);
        return new DailyCapacitySnapshot
        {
            Date = date,
            DateFull = dateFull,
            DayOfWeek = dayOfWeek,
            Total = total,
            OkCount = ok,
            NgCount = ng,
            Yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0.0%",
            DayShiftTotal = total * 6 / 10,
            DayShiftOk = ok * 6 / 10,
            DayShiftNg = ng * 6 / 10,
            NightShiftTotal = total - total * 6 / 10,
            NightShiftOk = ok - ok * 6 / 10,
            NightShiftNg = ng - ng * 6 / 10
        };
    }

    private static CapacityViewResult ToResult(List<DailyCapacitySnapshot> rows, int divisor)
    {
        var total = rows.Sum(static row => row.Total);
        var ok = rows.Sum(static row => row.OkCount);
        var ng = rows.Sum(static row => row.NgCount);
        return CapacityViewResult.Success(
            rows,
            total,
            ok,
            ng,
            total > 0 ? $"{ok * 100.0 / total:F2}%" : "0%",
            $"{total / Math.Max(1, divisor)}");
    }
}
