using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Shell.Features.Footer;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class FooterViewModelBehaviorTests
{
    [Fact]
    public Task FooterViewModel_ShouldRenderCloudAndMesSummaries()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            diagnosticsQuery.Current = CreateSnapshot(
                "Edge-A",
                new CloudSyncDiagnosticsSnapshot(
                    GateState: EdgeUploadGateState.Ready,
                    BlockReason: EdgeUploadBlockReason.None,
                    RuntimeState: CloudRetryRuntimeState.Idle,
                    LastAttemptAt: DateTime.Now,
                    LastSuccessAt: DateTime.Now,
                    LastFailureAt: null,
                    LastOutcome: CloudCallOutcome.Success,
                    LastReasonCode: "success",
                    LastProcessType: "Injection",
                    PendingRetryCount: 0,
                    PendingDeviceLogCount: 0,
                    PendingCapacityCount: 0,
                    IsPausedWaitingForRecovery: false,
                    IsCapacityBlocked: false,
                    BlockedChannel: null,
                    BlockedReason: "none",
                    LastCapacityBlockAt: null,
                    IsPersistenceFaulted: false,
                    LastPersistenceFaultAt: null,
                    PersistenceFaultMessage: null),
                new MesSyncDiagnosticsSnapshot(
                    RuntimeState: MesRetryRuntimeState.Idle,
                    LastAttemptAt: null,
                    LastSuccessAt: null,
                    LastFailureAt: null,
                    LastFailureReason: null,
                    PendingRetryCount: 0,
                    Channels: [],
                    IsCapacityBlocked: false,
                    BlockedChannel: null,
                    BlockedReason: "none",
                    LastCapacityBlockAt: null,
                    IsPersistenceFaulted: false,
                    LastPersistenceFaultAt: null,
                    PersistenceFaultMessage: null));

            var viewModel = new FooterViewModel(diagnosticsQuery);
            await viewModel.RefreshDiagnosticsAsync();

            Assert.Equal("Edge-A", viewModel.DeviceName);
            Assert.Equal("Cloud: Ready", viewModel.CloudStatus);
            Assert.Equal("MES: Idle", viewModel.MesStatus);

            diagnosticsQuery.Current = CreateSnapshot(
                "Edge-A",
                diagnosticsQuery.Current.Cloud with
                {
                    GateState = EdgeUploadGateState.Blocked,
                    BlockReason = EdgeUploadBlockReason.UploadTokenRejected,
                    IsPausedWaitingForRecovery = true
                },
                diagnosticsQuery.Current.Mes with
                {
                    RuntimeState = MesRetryRuntimeState.Backoff
                });

            await viewModel.RefreshDiagnosticsAsync();

            Assert.Equal("Cloud: Waiting for Recovery", viewModel.CloudStatus);
            Assert.Equal("MES: Retry Backoff", viewModel.MesStatus);

            diagnosticsQuery.Current = CreateSnapshot(
                "Edge-A",
                diagnosticsQuery.Current.Cloud with
                {
                    GateState = EdgeUploadGateState.Blocked,
                    BlockReason = EdgeUploadBlockReason.BootstrapTimeout,
                    IsPausedWaitingForRecovery = false
                },
                diagnosticsQuery.Current.Mes with
                {
                    RuntimeState = MesRetryRuntimeState.LastFailed
                });

            await viewModel.RefreshDiagnosticsAsync();

            Assert.Equal("Cloud: Blocked (bootstrap timeout)", viewModel.CloudStatus);
            Assert.Equal("MES: Last Failed", viewModel.MesStatus);
        });

    [Fact]
    public Task FooterViewModel_WhenCapacityBlocked_ShouldRenderLightweightBlockedStatus()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            diagnosticsQuery.Current = CreateSnapshot(
                "Edge-B",
                new CloudSyncDiagnosticsSnapshot(
                    GateState: EdgeUploadGateState.Ready,
                    BlockReason: EdgeUploadBlockReason.None,
                    RuntimeState: CloudRetryRuntimeState.Idle,
                    LastAttemptAt: null,
                    LastSuccessAt: null,
                    LastFailureAt: null,
                    LastOutcome: CloudCallOutcome.Success,
                    LastReasonCode: "success",
                    LastProcessType: null,
                    PendingRetryCount: 12,
                    PendingDeviceLogCount: 0,
                    PendingCapacityCount: 0,
                    IsPausedWaitingForRecovery: false,
                    IsCapacityBlocked: true,
                    BlockedChannel: CapacityBlockedChannel.Retry,
                    BlockedReason: "total",
                    LastCapacityBlockAt: DateTime.Now,
                    IsPersistenceFaulted: false,
                    LastPersistenceFaultAt: null,
                    PersistenceFaultMessage: null),
                new MesSyncDiagnosticsSnapshot(
                    RuntimeState: MesRetryRuntimeState.Backoff,
                    LastAttemptAt: null,
                    LastSuccessAt: null,
                    LastFailureAt: null,
                    LastFailureReason: null,
                    PendingRetryCount: 5,
                    Channels: [],
                    IsCapacityBlocked: true,
                    BlockedChannel: CapacityBlockedChannel.Fallback,
                    BlockedReason: "process_type",
                    LastCapacityBlockAt: DateTime.Now,
                    IsPersistenceFaulted: false,
                    LastPersistenceFaultAt: null,
                    PersistenceFaultMessage: null));

            var viewModel = new FooterViewModel(diagnosticsQuery);
            await viewModel.RefreshDiagnosticsAsync();

            Assert.Equal("Cloud: Capacity Blocked", viewModel.CloudStatus);
            Assert.Equal("MES: Capacity Blocked", viewModel.MesStatus);
        });

    [Fact]
    public Task FooterViewModel_WhenPersistenceFaulted_ShouldRenderStorageFaultStatus()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            diagnosticsQuery.Current = CreateSnapshot(
                "Edge-C",
                new CloudSyncDiagnosticsSnapshot(
                    GateState: EdgeUploadGateState.Ready,
                    BlockReason: EdgeUploadBlockReason.None,
                    RuntimeState: CloudRetryRuntimeState.Idle,
                    LastAttemptAt: null,
                    LastSuccessAt: null,
                    LastFailureAt: null,
                    LastOutcome: CloudCallOutcome.Success,
                    LastReasonCode: "success",
                    LastProcessType: null,
                    PendingRetryCount: 0,
                    PendingDeviceLogCount: 0,
                    PendingCapacityCount: 0,
                    IsPausedWaitingForRecovery: false,
                    IsCapacityBlocked: false,
                    BlockedChannel: null,
                    BlockedReason: "none",
                    LastCapacityBlockAt: null,
                    IsPersistenceFaulted: true,
                    LastPersistenceFaultAt: DateTime.Now,
                    PersistenceFaultMessage: "cloud retry count failed"),
                new MesSyncDiagnosticsSnapshot(
                    RuntimeState: MesRetryRuntimeState.Idle,
                    LastAttemptAt: null,
                    LastSuccessAt: null,
                    LastFailureAt: null,
                    LastFailureReason: null,
                    PendingRetryCount: 0,
                    Channels: [],
                    IsCapacityBlocked: false,
                    BlockedChannel: null,
                    BlockedReason: "none",
                    LastCapacityBlockAt: null,
                    IsPersistenceFaulted: true,
                    LastPersistenceFaultAt: DateTime.Now,
                    PersistenceFaultMessage: "mes retry count failed"));

            var viewModel = new FooterViewModel(diagnosticsQuery);
            await viewModel.RefreshDiagnosticsAsync();

            Assert.Equal("Cloud: Storage Fault", viewModel.CloudStatus);
            Assert.Equal("MES: Storage Fault", viewModel.MesStatus);
        });

    [Fact]
    public Task FooterViewModel_WhenRefreshReenters_ShouldOnlyRunOneDiagnosticsQuery()
        => RunOnStaThreadAsync(async () =>
        {
            var diagnosticsQuery = new FakeEdgeSyncDiagnosticsQuery();
            var viewModel = new FooterViewModel(diagnosticsQuery);
            await viewModel.RefreshDiagnosticsAsync();

            diagnosticsQuery.ResetCounters();
            diagnosticsQuery.Delay = TimeSpan.FromMilliseconds(120);

            var first = viewModel.RefreshDiagnosticsAsync();
            var second = viewModel.RefreshDiagnosticsAsync();
            await Task.WhenAll(first, second);

            Assert.Equal(1, diagnosticsQuery.TotalCalls);
            Assert.Equal(1, diagnosticsQuery.MaxConcurrentCalls);
        });

    private static EdgeSyncDiagnosticsSnapshot CreateSnapshot(
        string deviceName,
        CloudSyncDiagnosticsSnapshot cloud,
        MesSyncDiagnosticsSnapshot mes)
        => new(deviceName, cloud, mes, new ProductionContextPersistenceDiagnostics(0, null));

    private static Task RunOnStaThreadAsync(Func<Task> testBody)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await testBody();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completion.Task;
    }

    private sealed class FakeEdgeSyncDiagnosticsQuery : IEdgeSyncDiagnosticsQuery
    {
        private int _activeCalls;
        private int _maxConcurrentCalls;
        private int _totalCalls;

        public EdgeSyncDiagnosticsSnapshot Current { get; set; } = new(
            "Unknown",
            new CloudSyncDiagnosticsSnapshot(
                EdgeUploadGateState.Unknown,
                EdgeUploadBlockReason.DeviceUnidentified,
                CloudRetryRuntimeState.Idle,
                null,
                null,
                null,
                CloudCallOutcome.Success,
                "none",
                null,
                0,
                0,
                0,
                false,
                false,
                null,
                "none",
                null,
                false,
                null,
                null),
            new MesSyncDiagnosticsSnapshot(
                MesRetryRuntimeState.Idle,
                null,
                null,
                null,
                null,
                0,
                [],
                false,
                null,
                "none",
                null,
                false,
                null,
                null),
            new ProductionContextPersistenceDiagnostics(0, null));

        public TimeSpan Delay { get; set; }

        public int MaxConcurrentCalls => _maxConcurrentCalls;

        public int TotalCalls => _totalCalls;

        public void ResetCounters()
        {
            _activeCalls = 0;
            _maxConcurrentCalls = 0;
            _totalCalls = 0;
        }

        public async Task<EdgeSyncDiagnosticsSnapshot> GetCurrentAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _totalCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaxConcurrentCalls(active);

            try
            {
                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, ct);
                }

                return Current;
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        private void UpdateMaxConcurrentCalls(int active)
        {
            while (true)
            {
                var current = _maxConcurrentCalls;
                if (active <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentCalls, active, current) == current)
                {
                    return;
                }
            }
        }
    }
}
