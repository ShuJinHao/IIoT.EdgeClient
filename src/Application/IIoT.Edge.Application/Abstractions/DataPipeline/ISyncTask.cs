namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// 通用定时同步任务接口。
///
/// 同步任务通常承担两类职责：
/// 1. 启动和停止定时同步循环。
/// 2. 在 RetryTask 调度下补传本地 SQLite 缓冲。
/// </summary>
public interface ISyncTask
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync();

    /// <summary>
    /// 补传 SQLite 离线缓冲，由 RetryTask 调用。
    /// </summary>
    /// <returns>本轮是否全部补传成功。</returns>
    Task<bool> RetryBufferAsync();
}
