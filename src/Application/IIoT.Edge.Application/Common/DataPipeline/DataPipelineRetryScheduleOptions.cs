namespace IIoT.Edge.Application.Common.DataPipeline;

/// <summary>
/// Cloud/MES 补传任务、首次入队、人工重新入队和失败退避共用的调度配置。
/// </summary>
public sealed class DataPipelineRetryScheduleOptions
{
    public const string SectionName = "DataPipelineRetry";
    public const int DefaultIntervalMinutes = 30;

    public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;

    public TimeSpan GetInterval()
        => TimeSpan.FromMinutes(Math.Clamp(IntervalMinutes, 1, 24 * 60));
}
