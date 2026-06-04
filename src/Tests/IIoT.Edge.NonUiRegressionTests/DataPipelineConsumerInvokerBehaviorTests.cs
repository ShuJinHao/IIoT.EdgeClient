using IIoT.Edge.Host.DataPipeline.Services;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DataPipelineConsumerInvokerBehaviorTests
{
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
    public async Task ExecuteAsync_WhenActionExceedsTimeout_ShouldThrowTimeoutExceeded()
    {
        var invoker = new DefaultDataPipelineConsumerInvoker();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            invoker.ExecuteAsync(
                async ct =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return true;
                },
                TimeSpan.FromMilliseconds(20),
                CancellationToken.None));

        Assert.Equal("timeout_exceeded", exception.Message);
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
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    return true;
                },
                TimeSpan.FromSeconds(5),
                cts.Token));
    }
}
