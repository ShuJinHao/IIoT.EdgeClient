using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.AvaloniaShell.Services;
using IIoT.Edge.Host.Bootstrap.Core;
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

        var result = await coordinator.StartAsync([], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.False(result.RuntimeStarted);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.UiOnly, runtimeState.Snapshot.Status);
        Assert.Equal(0, lifecycle.StartCount);
    }

    [Fact]
    public async Task StartAsync_WithRuntimeArgument_ShouldStartLifecycle()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);

        var result = await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.RuntimeStarted);
        Assert.True(runtimeState.IsRuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.Running, runtimeState.Snapshot.Status);
        Assert.Equal(1, lifecycle.StartCount);
    }

    [Fact]
    public async Task StartAsync_WithRuntimeArgument_ShouldPublishDiagnosticsSummary()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var runtimePaths = CreateRuntimePaths();
        var coordinator = new AvaloniaShellStartupCoordinator(
            lifecycle,
            runtimeState,
            new StubStartupDiagnosticsStore(CreateStartupDiagnosticsReport()),
            runtimePaths);

        var result = await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("模块数：1", result.DiagnosticsSummary, StringComparison.Ordinal);
        Assert.Contains("PLC 设备数：1", result.DiagnosticsSummary, StringComparison.Ordinal);
        Assert.Contains(runtimePaths.RuntimeDataRoot, runtimeState.Snapshot.DiagnosticsSummary, StringComparison.Ordinal);
        Assert.Equal(runtimePaths.LogDirectory, result.DiagnosticsLogPath);
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

        var result = await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(result.RuntimeStarted);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.StartFailed, runtimeState.Snapshot.Status);
        Assert.Equal("启动校验失败。", result.Message);
    }

    [Fact]
    public async Task StopAsync_WhenRuntimeStarted_ShouldStopLifecycle()
    {
        var lifecycle = new StubLifecycleCoordinator();
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);
        await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        var stopped = await coordinator.StopAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(stopped);
        Assert.False(runtimeState.IsRuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.UiOnly, runtimeState.Snapshot.Status);
        Assert.Equal(1, lifecycle.StopCount);
    }

    [Fact]
    public async Task StopAsync_WhenLifecycleDoesNotStopWithinTimeout_ShouldReturnFalse()
    {
        var lifecycle = new StubLifecycleCoordinator { BlockStopUntilCanceled = true };
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);
        await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        var stopped = await coordinator.StopAsync(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);

        Assert.False(stopped);
        Assert.True(runtimeState.IsRuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.Stopping, runtimeState.Snapshot.Status);
    }

    [Fact]
    public async Task StartAsync_WhenLifecycleThrows_ShouldReturnFailureWithErrorDetail()
    {
        var lifecycle = new StubLifecycleCoordinator { ExceptionToThrow = new InvalidOperationException("配置缺失") };
        var runtimeState = new AvaloniaRuntimeState();
        var coordinator = new AvaloniaShellStartupCoordinator(lifecycle, runtimeState);

        var result = await coordinator.StartAsync(["--start-runtime"], TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(result.RuntimeStarted);
        Assert.Equal(AvaloniaRuntimeStatus.StartFailed, runtimeState.Snapshot.Status);
        Assert.Contains("配置缺失", result.Message, StringComparison.Ordinal);
    }

    private sealed class StubLifecycleCoordinator : IAppLifecycleCoordinator
    {
        public AppStartupResult StartupResult { get; set; } = AppStartupResult.Ok();

        public bool BlockStopUntilCanceled { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public Task<AppStartupResult> StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

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

    private static EdgeRuntimePaths CreateRuntimePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "iiot-edge-avalonia-startup-tests", Guid.NewGuid().ToString("N"));
        return new EdgeRuntimePaths(
            BaseDirectory: root,
            ProfileName: "AvaloniaShellTests",
            RuntimeDataRoot: root,
            DatabaseDirectory: Path.Combine(root, "db"),
            ContextDirectory: Path.Combine(root, "context"),
            RecipeDirectory: Path.Combine(root, "recipe"),
            ExcelDirectory: Path.Combine(root, "excel"),
            DiagnosticsDirectory: Path.Combine(root, "diagnostics"),
            LogDirectory: Path.Combine(root, "diagnostics", "logs"),
            DeviceCacheFilePath: Path.Combine(root, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(root, "diagnostics", "crash.log"),
            FallbackCrashLogPath: Path.Combine(root, "diagnostics", "crash.fallback.log"));
    }

    private static StartupDiagnosticsReport CreateStartupDiagnosticsReport()
        => new(
            DateTime.Now,
            new ConfigurationProfileSnapshot("AvaloniaShellTests", "line-a", "line-a.json", true, "runtime-data"),
            ["Homogenization"],
            ["Homogenization"],
            ["Homogenization"],
            [new PluginLifecycleSnapshot("Homogenization", "匀浆", "Homogenization", "1.0.0", PluginLifecycleState.Activated, "已激活")],
            [new ModuleRegistrationSnapshot("Homogenization", "Homogenization", "IIoT.Edge.Module.Homogenization.Avalonia", true, true, true, true, true, true)],
            [new DeviceModuleBindingSnapshot("PLC-01", "Homogenization", true, true, true)],
            []);

    private sealed class StubStartupDiagnosticsStore(StartupDiagnosticsReport report) : IStartupDiagnosticsStore
    {
        public StartupDiagnosticsReport Current { get; private set; } = report;

        public void Update(StartupDiagnosticsReport report) => Current = report;
    }
}
