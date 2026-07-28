using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Application.Features.Updates;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class RuntimeHeartbeatServiceBehaviorTests
{
    [Fact]
    public async Task StartAsync_WhenBootstrapFails_ShouldNotThrowOrBlockStartup()
    {
        var logger = new FakeLogService();
        var bootstrapFailureLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.EntryAdded += entry =>
        {
            if (entry.Message.Contains("Bootstrap 失败", StringComparison.Ordinal))
            {
                bootstrapFailureLogged.TrySetResult();
            }
        };
        var service = new EdgeRuntimeHeartbeatService(
            new FixedUpdateConfigurationProvider(),
            new FailingDeviceSessionClient(),
            new RecordingRuntimeHeartbeatReporter(),
            new FixedRuntimeConfigService(),
            logger);

        await service.StartAsync(
            new EdgeUpdateTarget("LineA", AppContext.BaseDirectory, string.Empty),
            TestContext.Current.CancellationToken);
        await bootstrapFailureLogged.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(logger.Entries, entry =>
            entry.Message.Contains("Bootstrap", StringComparison.Ordinal)
            || entry.Message.Contains("运行心跳", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_WhenCloudBusinessSwitchIsDisabled_ShouldMakeZeroCloudRequests()
    {
        var logger = new FakeLogService();
        var loopStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        logger.EntryAdded += entry =>
        {
            if (entry.Message.Contains("循环已启动", StringComparison.Ordinal))
            {
                loopStarted.TrySetResult();
            }
        };
        var sessionClient = new CountingDeviceSessionClient();
        var reporter = new CountingRuntimeHeartbeatReporter();
        var service = new EdgeRuntimeHeartbeatService(
            new FixedUpdateConfigurationProvider(),
            sessionClient,
            reporter,
            new FixedRuntimeConfigService(systemCloudEnabled: false),
            logger);

        await service.StartAsync(
            new EdgeUpdateTarget("LineA", AppContext.BaseDirectory, string.Empty),
            TestContext.Current.CancellationToken);
        await loopStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sessionClient.BootstrapCallCount);
        Assert.Equal(0, reporter.ReportCallCount);
    }

    [Fact]
    public async Task StartAsync_WhenHeartbeatPathIsMissing_ShouldSkipWithoutBootstrapRequest()
    {
        var logger = new FakeLogService();
        var pathDiagnosticLogged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        logger.EntryAdded += entry =>
        {
            if (entry.Message.Contains("上报路径未配置", StringComparison.Ordinal))
            {
                pathDiagnosticLogged.TrySetResult();
            }
        };
        var sessionClient = new CountingDeviceSessionClient();
        var reporter = new CountingRuntimeHeartbeatReporter();
        var service = new EdgeRuntimeHeartbeatService(
            new FixedUpdateConfigurationProvider(runtimeHeartbeatPath: string.Empty),
            sessionClient,
            reporter,
            new FixedRuntimeConfigService(),
            logger);

        await service.StartAsync(
            new EdgeUpdateTarget("LineA", AppContext.BaseDirectory, string.Empty),
            TestContext.Current.CancellationToken);
        await pathDiagnosticLogged.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sessionClient.BootstrapCallCount);
        Assert.Equal(0, reporter.ReportCallCount);
    }

    private sealed class FixedUpdateConfigurationProvider(
        string runtimeHeartbeatPath = "/api/v1/edge/runtime-heartbeats")
        : IEdgeUpdateConfigurationProvider
    {
        public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
            => EdgeUpdateConfigurationResult.Succeeded(new EdgeUpdateCloudApiOptions(
                "https://cloud.example.test",
                1,
                "DEV-001",
                "secret",
                "/api/v1/bootstrap/device-instance",
                "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                "/api/v1/edge/client-releases/version-reports",
                runtimeHeartbeatPath));

        public EdgeReleaseOptions ResolveReleaseOptions()
            => new("stable", "win-x64");
    }

    private sealed class FailingDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Failed("bootstrap down"));
    }

    private sealed class RecordingRuntimeHeartbeatReporter : IEdgeRuntimeHeartbeatReporter
    {
        public Task<EdgeRuntimeHeartbeatReportResult> ReportAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeRuntimeHeartbeatReport report,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeRuntimeHeartbeatReportResult.Succeeded());
    }

    private sealed class CountingDeviceSessionClient : IEdgeUpdateDeviceSessionClient
    {
        public int BootstrapCallCount { get; private set; }

        public Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
            EdgeUpdateCloudApiOptions options,
            CancellationToken cancellationToken = default)
        {
            BootstrapCallCount++;
            return Task.FromResult(
                EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Succeeded(
                    new EdgeUpdateDeviceSession(
                        Guid.NewGuid(),
                        "LineA",
                        options.ClientCode,
                        "token")));
        }
    }

    private sealed class CountingRuntimeHeartbeatReporter : IEdgeRuntimeHeartbeatReporter
    {
        public int ReportCallCount { get; private set; }

        public Task<EdgeRuntimeHeartbeatReportResult> ReportAsync(
            EdgeUpdateCloudApiOptions options,
            EdgeUpdateDeviceSession session,
            EdgeRuntimeHeartbeatReport report,
            CancellationToken cancellationToken = default)
        {
            ReportCallCount++;
            return Task.FromResult(EdgeRuntimeHeartbeatReportResult.Succeeded());
        }
    }

    private sealed class FixedRuntimeConfigService(
        bool systemCloudEnabled = true) : ILocalSystemRuntimeConfigService
    {
        public SystemRuntimeConfigSnapshot Current { get; } = SystemRuntimeConfigSnapshot.Default with
        {
            SystemCloudEnabled = systemCloudEnabled,
            RuntimeHeartbeatInterval = TimeSpan.FromSeconds(10)
        };

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
