using System;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Edge.SharedKernel.DataPipeline.Capacity;

/// <summary>
/// 班次配置。
/// 从 <c>appsettings.json</c> 的 <c>Shift</c> 节点绑定。
/// </summary>
public class ShiftConfig
{
    /// <summary>
    /// 白班开始时间，默认值为 08:30。
    /// </summary>
    public string DayStart { get; set; } = "08:30";

    /// <summary>
    /// 白班结束时间，同时也是夜班开始时间，默认值为 20:30。
    /// </summary>
    public string DayEnd { get; set; } = "20:30";

    /// <summary>
    /// 解析后的白班开始时间。
    /// </summary>
    public TimeSpan DayStartTime =>
        TimeSpan.TryParse(DayStart, out var ts) ? ts : new TimeSpan(8, 30, 0);

    /// <summary>
    /// 解析后的白班结束时间。
    /// </summary>
    public TimeSpan DayEndTime =>
        TimeSpan.TryParse(DayEnd, out var ts) ? ts : new TimeSpan(20, 30, 0);
}
