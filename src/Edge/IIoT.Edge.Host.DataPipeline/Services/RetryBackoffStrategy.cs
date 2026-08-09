using IIoT.Edge.Application.Common.DataPipeline;

namespace IIoT.Edge.Host.DataPipeline.Services;

public sealed class DefaultRetryBackoffStrategy(
    DataPipelineRetryScheduleOptions? scheduleOptions = null) : IRetryBackoffStrategy
{
    private readonly TimeSpan _retryInterval =
        (scheduleOptions ?? new DataPipelineRetryScheduleOptions()).GetInterval();

    public TimeSpan Calculate(int retryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);
        return _retryInterval;
    }
}
