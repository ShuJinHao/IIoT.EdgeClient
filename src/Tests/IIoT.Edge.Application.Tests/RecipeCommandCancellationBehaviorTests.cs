using IIoT.Edge.Application.Features.Formula.RecipeView;

namespace IIoT.Edge.Application.Tests;

public sealed class RecipeCommandCancellationBehaviorTests
{
    [Fact]
    public async Task Handle_WhenCloudPullIsInFlightAndCallerCancels_ShouldPropagateExactToken()
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
        var handler = new SyncRecipeFromCloudHandler(recipeService);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        var handleTask = handler.Handle(new SyncRecipeFromCloudCommand(), cts.Token);
        await pullStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handleTask);
        Assert.Equal(1, recipeService.PullFromCloudCallCount);
        Assert.Equal(cts.Token, recipeService.LastPullCancellationToken);
    }

    [Fact]
    public async Task SaveLocalParam_WhenPersistenceFails_ShouldPropagateExactFailure()
    {
        var failure = new IOException("recipe disk full");
        var recipeService = new FakeRecipeService
        {
            SetLocalParamHandler = (_, _, _, _) => throw failure
        };
        var handler = new SaveLocalRecipeParamHandler(recipeService);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            handler.Handle(
                new SaveLocalRecipeParamCommand("Voltage", 1, 2, "V"),
                TestContext.Current.CancellationToken));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task DeleteLocalParam_WhenPersistenceFails_ShouldPropagateExactFailure()
    {
        var failure = new IOException("recipe replace blocked");
        var recipeService = new FakeRecipeService
        {
            RemoveLocalParamHandler = _ => throw failure
        };
        var handler = new DeleteLocalRecipeParamHandler(recipeService);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            handler.Handle(
                new DeleteLocalRecipeParamCommand("Voltage"),
                TestContext.Current.CancellationToken));

        Assert.Same(failure, actual);
    }
}
