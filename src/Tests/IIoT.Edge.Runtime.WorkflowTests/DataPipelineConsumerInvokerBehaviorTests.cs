using IIoT.Edge.Host.DataPipeline.Services;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class DataPipelineConsumerInvokerBehaviorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public async Task ExecuteAsync_WhenCallerIsAlreadyCanceled_ShouldNotInvokeAction(int timeoutMilliseconds)
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var calls = 0;

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.ExecuteAsync(
            _ =>
            {
                calls++;
                return Task.FromResult(true);
            },
            TimeSpan.FromMilliseconds(timeoutMilliseconds),
            cts.Token));

        Assert.Equal(cts.Token, actual.CancellationToken);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5000)]
    public async Task ExecuteAsync_WhenCallerCancelsBeforeIgnoredActionReturns_ShouldPropagateCancellation(int timeoutMilliseconds)
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();
        using var cts = new CancellationTokenSource();
        var actionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReturn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = invoker.ExecuteAsync(
            async _ =>
            {
                actionStarted.TrySetResult();
                await allowReturn.Task;
                return true;
            },
            TimeSpan.FromMilliseconds(timeoutMilliseconds),
            cts.Token);
        await actionStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        allowReturn.TrySetResult();

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(cts.Token, actual.CancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTimeoutIsDisabled_ShouldUseCallerCancellationToken()
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();
        using var cts = new CancellationTokenSource();
        CancellationToken observedToken = default;

        var result = await invoker.ExecuteAsync(
            ct =>
            {
                observedToken = ct;
                return Task.FromResult(1);
            },
            TimeSpan.Zero,
            cts.Token);

        Assert.Equal(1, result);
        Assert.Equal(cts.Token, observedToken);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTimeoutIsEnabled_ShouldPassCancelableTokenToAction()
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();
        CancellationToken observedToken = default;

        var result = await invoker.ExecuteAsync(
            ct =>
            {
                observedToken = ct;
                return Task.FromResult("ok");
            },
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.True(observedToken.CanBeCanceled);
    }

    [Fact]
    public async Task ExecuteAsync_WhenActionExceedsTimeout_ShouldThrowChineseTimeout()
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            invoker.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return true;
                },
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.Equal("处理超时。", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCallerCancels_ShouldNotConvertToTimeout()
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            invoker.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    return true;
                },
                TimeSpan.FromSeconds(5),
                cts.Token));
    }
}
