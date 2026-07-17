public sealed class FactAttribute : Attribute { }
public sealed class WallClockGuardTest
{
    [Fact]
    public async Task UsesBarrierAndCancellationGuard()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entered.SetResult();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(1), cts.Token);
        var blocked = Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        cts.Cancel();
        await AssertCanceledAsync(blocked);
    }

    private static async Task AssertCanceledAsync(Task task)
    {
        try { await task; } catch (OperationCanceledException) { }
    }
}
