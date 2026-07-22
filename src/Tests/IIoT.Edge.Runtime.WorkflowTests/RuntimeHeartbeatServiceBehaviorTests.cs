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

    private sealed class FixedUpdateConfigurationProvider : IEdgeUpdateConfigurationProvider
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
                "/api/v1/edge/runtime-heartbeats"));

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

    private sealed class FixedRuntimeConfigService : ILocalSystemRuntimeConfigService
    {
        public SystemRuntimeConfigSnapshot Current { get; } = SystemRuntimeConfigSnapshot.Default with
        {
            SystemCloudEnabled = true,
            RuntimeHeartbeatInterval = TimeSpan.FromSeconds(10)
        };

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
