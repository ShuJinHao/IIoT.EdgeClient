using IIoT.Edge.Runtime.DataPipeline.Tasks;
using System.Reflection;

namespace IIoT.Edge.TestSimulator.Tasks;

/// <summary>
/// 模拟场景可直接触发的 Cloud 重试任务包装器。
/// 仅用于模拟器里手动执行一轮补传。
/// </summary>
public sealed class TestRetryTask
{
    private static readonly MethodInfo ExecuteAsyncMethod = typeof(CloudRetryTask)
        .GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        ?? throw new InvalidOperationException("CloudRetryTask.ExecuteAsync was not found.");

    private readonly CloudRetryTask _inner;

    public TestRetryTask(CloudRetryTask inner)
    {
        _inner = inner;
    }

    public Task TriggerAsync()
        => (Task)(ExecuteAsyncMethod.Invoke(_inner, null)
            ?? throw new InvalidOperationException("CloudRetryTask.ExecuteAsync returned null."));
}
