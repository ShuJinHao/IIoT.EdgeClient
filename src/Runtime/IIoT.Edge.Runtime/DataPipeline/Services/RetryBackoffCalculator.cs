namespace IIoT.Edge.Runtime.DataPipeline.Services;

/// <summary>
/// DataPipeline 自动补传的统一退避计算器，Cloud/MES 共用同一策略。
/// </summary>
internal static class RetryBackoffCalculator
{
    public static TimeSpan Calculate(int retryCount)
    {
        if (retryCount <= 5)
        {
            return TimeSpan.FromSeconds(30);
        }

        if (retryCount <= 10)
        {
            return TimeSpan.FromMinutes(5);
        }

        return TimeSpan.FromMinutes(30);
    }
}
