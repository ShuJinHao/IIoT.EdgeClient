namespace IIoT.Edge.Host.DataPipeline.Services;

/// <summary>
/// 统一封装 DataPipeline 消费者调用的超时和取消语义。
/// </summary>
public interface IDataPipelineConsumerInvoker
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
