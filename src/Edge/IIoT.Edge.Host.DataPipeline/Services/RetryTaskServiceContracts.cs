namespace IIoT.Edge.Host.DataPipeline.Services;

public interface IRetryTaskFallbackRecoveryService
{
    Task RecoverAsync();
}

public interface IRetryTaskRecordProcessor<TProcessResult>
{
    Task<TProcessResult> ProcessAsync(CancellationToken cancellationToken);
}

public interface IRetryTaskHousekeepingService
{
    Task RecoverAbandonedRecordsAsync();

    Task CleanupExpiredAbandonedRecordsAsync();

    Task ApplyIdleOrBackoffStateAsync();
}
