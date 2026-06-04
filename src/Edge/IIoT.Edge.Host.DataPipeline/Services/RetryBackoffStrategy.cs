namespace IIoT.Edge.Host.DataPipeline.Services;

public sealed class DefaultRetryBackoffStrategy : IRetryBackoffStrategy
{
    public TimeSpan Calculate(int retryCount)
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
