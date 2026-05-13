namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能列表行视图模型。
/// 日查询时，每行对应一个半小时槽位。
/// 月查询时，每行对应一天。
/// 年查询时，每行对应一个月份。
/// </summary>
public class DailyCapacityVm
{
    public string Date { get; set; } = "";
    public string DateFull { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public int Total { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public string Yield { get; set; } = "";
    public int DayShiftTotal { get; set; }
    public int DayShiftOk { get; set; }
    public int DayShiftNg { get; set; }
    public int NightShiftTotal { get; set; }
    public int NightShiftOk { get; set; }
    public int NightShiftNg { get; set; }
}

/// <summary>
/// 半小时槽位视图模型，用于日查询明细。
/// </summary>
public class HourlySlotVm
{
    public int SlotOrder { get; set; }
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int StartHour { get; set; }
    public int StartMinute { get; set; }
    public string TimeLabel { get; set; } = "";
    public string ShiftCode { get; set; } = "";
    public int TotalCount { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
}

/// <summary>
/// 日汇总视图模型，用于日查询回退场景以及月、年聚合的基础数据。
/// </summary>
public class DailySummaryVm
{
    public int TotalCount { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public int DayShiftTotal { get; set; }
    public int DayShiftOk { get; set; }
    public int DayShiftNg { get; set; }
    public int NightShiftTotal { get; set; }
    public int NightShiftOk { get; set; }
    public int NightShiftNg { get; set; }
}

/// <summary>
/// 图表柱状项视图模型。
/// </summary>
public class CapacityChartBarVm
{
    public string Label { get; set; } = "";
    public int Value { get; set; }
    public double HeightRatio { get; set; }
    public double ChartHeight { get; set; }
}
