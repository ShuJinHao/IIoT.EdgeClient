using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public static class PlcRuntimeSingleTaskFactory
{
    public static IPlcTask CreateRequired(
        string taskKey,
        Func<IReadOnlySet<string>, List<IPlcTask>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskKey);
        ArgumentNullException.ThrowIfNull(factory);

        var normalizedTaskKey = taskKey.Trim();
        var tasks = factory(
                        new HashSet<string>(
                            [normalizedTaskKey],
                            StringComparer.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException(
                        $"业务任务工厂请求 {normalizedTaskKey} 时返回了 null 集合。");
        if (tasks.Count != 1)
        {
            throw new InvalidOperationException(
                $"业务任务工厂请求 {normalizedTaskKey} 时返回 {tasks.Count} 个任务，必须且只能返回一个。");
        }

        var task = tasks[0]
                   ?? throw new InvalidOperationException(
                       $"业务任务工厂请求 {normalizedTaskKey} 时返回了 null。");
        if (!string.Equals(
                task.TaskName,
                normalizedTaskKey,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"业务任务工厂请求 {normalizedTaskKey}，但返回任务名 {task.TaskName}。");
        }

        return task;
    }
}
