namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// 默认消费者调用器：内部超时统一转换为中文超时错误，外部取消保持原始取消语义。
/// </summary>
public sealed class DefaultDataPipelineConsumerInvoker : IDataPipelineConsumerInvoker
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (timeout <= TimeSpan.Zero)
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var result = await action(timeoutCts.Token)
                .WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException("处理超时。");
        }
    }
}
