using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Recipe;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class RecipeSyncTaskCancellationBehaviorTests
{
    [Fact]
    public async Task StartAsync_WhenCloudPullIsInFlightAndCallerCancels_ShouldStopWithoutSuccessOrFailureSideEffects()
    {
        var pullStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recipeService = new FakeRecipeService
        {
            PullFromCloudHandler = async cancellationToken =>
            {
                pullStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
        };
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-A",
            ClientCode = "LINE-01",
            ProcessId = Guid.NewGuid()
        });
        var logger = new FakeLogService();
        var task = new RecipeSyncTask(
            recipeService,
            deviceService,
            logger,
            TimeSpan.Zero);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var backgroundTask = task.StartAsync(cts.Token);
        await pullStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await backgroundTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, recipeService.PullFromCloudCallCount);
        Assert.Equal(cts.Token, recipeService.LastPullCancellationToken);
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("缓存已刷新", StringComparison.Ordinal)
                     || entry.Message.Contains("同步失败", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Message.Contains("已停止", StringComparison.Ordinal));
    }
}
