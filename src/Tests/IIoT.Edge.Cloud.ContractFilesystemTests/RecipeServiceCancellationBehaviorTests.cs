using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Recipe;

namespace IIoT.Edge.Cloud.ContractFilesystemTests;

public sealed class RecipeServiceCancellationBehaviorTests
{
    [Fact]
    public async Task PullFromCloudAsync_WhenHttpIsInFlightAndCallerCancels_ShouldNotCompleteOrPersistCache()
    {
        var getStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloudHttp = new FakeCloudHttpClient
        {
            GetStarted = getStarted,
            GetWait = neverRelease.Task
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
        var recipeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-recipe-cancel-{Guid.NewGuid():N}");
        var changedCount = 0;

        try
        {
            var service = new RecipeService(
                cloudHttp,
                new FakeCloudApiEndpointProvider(),
                deviceService,
                logger,
                recipeDirectory);
            service.RecipeChanged += () => changedCount++;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

            var pullTask = service.PullFromCloudAsync(cts.Token);
            await getStarted.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pullTask);
            Assert.Equal(1, cloudHttp.GetCallCount);
            Assert.Equal(0, cloudHttp.CompletedGetCount);
            Assert.Equal([cts.Token], cloudHttp.GetCancellationTokens);
            Assert.Null(service.CloudRecipe);
            Assert.Equal(0, changedCount);
            Assert.Empty(Directory.EnumerateFiles(recipeDirectory));
            Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Message.Contains("已加载", StringComparison.Ordinal)
                         || entry.Message.Contains("拉取失败", StringComparison.Ordinal)
                         || entry.Message.Contains("解析失败", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(recipeDirectory))
            {
                Directory.Delete(recipeDirectory, recursive: true);
            }
        }
    }
}
