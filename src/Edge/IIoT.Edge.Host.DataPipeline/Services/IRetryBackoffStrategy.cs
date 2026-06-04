namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// DataPipeline 自动补传的统一退避计算器，Cloud/MES 共用同一策略。
/// </summary>
public interface IRetryBackoffStrategy
{
    TimeSpan Calculate(int retryCount);
}
