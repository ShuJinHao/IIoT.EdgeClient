using IIoT.Edge.AvaloniaShell.Services;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.UI.Avalonia.Services;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class AvaloniaShellStartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_WithoutRuntimeArgument_ShouldKeepUiOnly()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);

        var result = await coordinator.StartAsync([]);

        Assert.True(result.Success);
        Assert.False(result.RuntimeStarted);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal(0, lifecycle.StartCount);
    }

    [Fact]
    public async Task StartAsync_WithRuntimeArgument_ShouldStartLifecycle()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);

        var result = await coordinator.StartAsync(["--start-runtime"]);

        Assert.True(result.Success);
        Assert.True(result.RuntimeStarted);
        Assert.True(runtimeState.IsRuntimeStarted);
        Assert.Equal(1, lifecycle.StartCount);
    }

    [Fact]
    public async Task StartAsync_WhenLifecycleFails_ShouldReturnFailure()
    {
        var lifecycle = new StubLifecycleCoordinator
        {
            StartupResult = AppStartupResult.Failure("启动校验失败。")
        };
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);

        var result = await coordinator.StartAsync(["--start-runtime"]);

        Assert.False(result.Success);
        Assert.False(result.RuntimeStarted);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal("启动校验失败。", result.Message);
    }

    [Fact]
    public async Task StopAsync_WhenRuntimeStarted_ShouldStopLifecycle()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);
        await coordinator.StartAsync(["--start-runtime"]);

        var stopped = await coordinator.StopAsync(TimeSpan.FromSeconds(1));

        Assert.True(stopped);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal(1, lifecycle.StopCount);
    }

    [Fact]
    public async Task StopAsync_WhenLifecycleDoesNotStopWithinTimeout_ShouldReturnFalse()
    {
        var lifecycle = new StubLifecycleCoordinator { BlockStopUntilCanceled = true };
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);
        await coordinator.StartAsync(["--start-runtime"]);

        var stopped = await coordinator.StopAsync(TimeSpan.FromMilliseconds(10));

        Assert.False(stopped);
        Assert.True(runtimeState.IsRuntimeStarted);
    }

    private sealed class StubLifecycleCoordinator : IAppLifecycleCoordinator
    {
        public AppStartupResult StartupResult { get; set; } = AppStartupResult.Ok();

        public bool BlockStopUntilCanceled { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<AppStartupResult> StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult(StartupResult);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            if (BlockStopUntilCanceled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }
}
