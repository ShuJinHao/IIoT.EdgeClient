namespace IIoT.Edge.Application.Abstractions.Tasks;

/// <summary>
/// 后台任务契约。
/// 统一定义后台任务的名称与启动入口。
/// 适用于所有后台运行的任务（PLC 任务、数据管道任务、定时任务等）。
/// </summary>
public interface IBackgroundTask
{
    string TaskName { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
