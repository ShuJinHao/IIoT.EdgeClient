namespace IIoT.Edge.Application.Abstractions.Tasks;

/// <summary>
/// 为长运行后台任务提供显式启动握手，避免用固定时间窗口猜测任务是否已启动。
/// </summary>
public interface IStartupAwareBackgroundTask : IBackgroundTask
{
    BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken);
}

public readonly record struct BackgroundTaskRun
{
    public BackgroundTaskRun(Task startup, Task execution)
    {
        Startup = startup ?? throw new ArgumentNullException(nameof(startup));
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    public Task Startup { get; }
    public Task Execution { get; }
}
