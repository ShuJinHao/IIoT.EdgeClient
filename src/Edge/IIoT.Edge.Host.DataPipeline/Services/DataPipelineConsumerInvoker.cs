namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// 默认消费者调用器：内部超时统一转换为 timeout_exceeded，外部取消保持原始取消语义。
/// </summary>
public sealed class DefaultDataPipelineConsumerInvoker : IDataPipelineConsumerInvoker
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (timeout <= TimeSpan.Zero)
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            return await action(timeoutCts.Token)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("timeout_exceeded");
        }
    }
}
