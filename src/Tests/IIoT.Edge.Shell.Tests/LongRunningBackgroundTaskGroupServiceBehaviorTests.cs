using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Common.Tasks;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class LongRunningBackgroundTaskGroupServiceBehaviorTests
{
    [Fact]
    public async Task StartAsync_WhenChildFailsDuringStartup_ShouldSurfaceException()
    {
        var service = new LongRunningBackgroundTaskGroupService(
            "test-group",
            [new FailingBackgroundTask()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));

        Assert.Equal("startup failed", exception.Message);
    }

    private sealed class FailingBackgroundTask : IBackgroundTask
    {
        public string TaskName => "FailingTask";

        public Task StartAsync(CancellationToken ct)
            => throw new InvalidOperationException("startup failed");
    }
}
