using System.Collections.Concurrent;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Shell.Core;
using IIoT.Edge.Testing;

namespace IIoT.Edge.Shell.UiTests;

public sealed class PlcTaskBindingTransactionBehaviorTests
{
    private static long _eventSequence;

    [Fact]
    public async Task SaveAndApply_WhenHardwarePermissionIsMissing_ShouldRejectBeforeMutation()
    {
        var persistence = new ControlledPersistenceTransaction();
        var runtime = new ControlledRuntimeTransaction();
        var service = CreateService(
            persistence,
            runtime,
            permissionService: new ControlledPermissionService(canEditHardware: false));

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveAndApplyAsync(
                1,
                "TestModule",
                States(("Task.MG1", true)),
                TestContext.Current.CancellationToken));

        Assert.Contains("硬件配置权限", exception.Message, StringComparison.Ordinal);
        Assert.Empty(persistence.PreparedDeviceIds);
        Assert.Equal(0, runtime.CaptureCalls);
        Assert.Equal(0, runtime.ApplyCalls);
    }

    [Fact]
    public async Task SaveAndApply_WhenPermissionIsRevokedWhileWaitingForMutationGate_ShouldRejectBeforeSnapshot()
    {
        var gate = new PlcRuntimeConfigurationMutationGate();
        using var hardwareMutation = await gate.EnterAsync(
            1,
            TestContext.Current.CancellationToken);
        var persistence = new ControlledPersistenceTransaction();
        var runtime = new ControlledRuntimeTransaction();
        var permissions = new ControlledPermissionService(canEditHardware: true);
        var service = CreateService(persistence, runtime, gate, permissions);

        var save = service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", true)),
            TestContext.Current.CancellationToken);
        permissions.CanEditHardware = false;
        hardwareMutation.Dispose();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => save);
        Assert.Empty(persistence.PreparedDeviceIds);
        Assert.Equal(0, runtime.CaptureCalls);
        Assert.Equal(0, runtime.ApplyCalls);
    }

    [Fact]
    public async Task SaveAndApply_WhenSqliteCommitFails_ShouldNotTouchRuntime()
    {
        var commitFailure = new IOException("sqlite commit failed");
        var persistence = new ControlledPersistenceTransaction
        {
            CommitFailure = commitFailure
        };
        var runtime = new ControlledRuntimeTransaction();
        var service = CreateService(persistence, runtime);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            service.SaveAndApplyAsync(
                1,
                "TestModule",
                States(("Task.MG1", true)),
                TestContext.Current.CancellationToken));

        Assert.Same(commitFailure, actual);
        Assert.Equal(1, runtime.CaptureCalls);
        Assert.Equal(0, runtime.ApplyCalls);
        Assert.Equal(0, runtime.RestoreCalls);
        Assert.Equal(0, persistence.RestoreCalls);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("checkpoint")]
    public async Task SaveAndApply_WhenRuntimeDeltaFails_ShouldRestoreRuntimeAndSqlite(
        string failureStage)
    {
        var primary = new InvalidOperationException($"{failureStage} failed");
        var persistence = new ControlledPersistenceTransaction();
        var runtime = new ControlledRuntimeTransaction
        {
            ApplyFailure = primary
        };
        var service = CreateService(persistence, runtime);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAndApplyAsync(
                1,
                "TestModule",
                States(("Task.MG1", true)),
                TestContext.Current.CancellationToken));

        Assert.Same(primary, actual);
        Assert.Equal(1, runtime.ApplyCalls);
        Assert.Equal(1, runtime.RestoreCalls);
        Assert.Equal(1, persistence.RestoreCalls);
        Assert.Equal(
            ["prepare:1", "commit:1", "runtime-apply:1", "runtime-restore:1", "restore:1"],
            persistence.Events.Concat(runtime.Events)
                .OrderBy(static item => item.Sequence)
                .Select(static item => item.Name));
    }

    [Fact]
    public async Task SaveAndApply_WhenBothRollbacksFail_ShouldReportExplicitTransactionFault()
    {
        var primary = new InvalidOperationException("runtime apply failed");
        var persistence = new ControlledPersistenceTransaction
        {
            RestoreFailure = new IOException("plugin database restore failed")
        };
        var runtime = new ControlledRuntimeTransaction
        {
            ApplyFailure = primary,
            RestoreFailure = new TimeoutException("runtime restore failed")
        };
        var service = CreateService(persistence, runtime);

        var actual = await Assert.ThrowsAsync<PlcTaskBindingTransactionException>(() =>
            service.SaveAndApplyAsync(
                1,
                "TestModule",
                States(("Task.MG1", true)),
                TestContext.Current.CancellationToken));

        Assert.Same(primary, actual.PrimaryFailure);
        Assert.Equal(2, actual.RollbackFailures.Count);
        Assert.Contains(actual.RollbackFailures, failure => failure.Message.Contains("运行任务组合", StringComparison.Ordinal));
        Assert.Contains(actual.RollbackFailures, failure => failure.Message.Contains("插件私库", StringComparison.Ordinal));
        Assert.Equal(1, runtime.RestoreCalls);
        Assert.Equal(1, persistence.RestoreCalls);
    }

    [Fact]
    public async Task SaveAndApply_WhenDisconnected_ShouldCommitAsWaitingForPlc()
    {
        var persistence = new ControlledPersistenceTransaction();
        var runtime = new ControlledRuntimeTransaction
        {
            ApplyState = PlcRuntimeTaskApplyState.WaitingForConnection
        };
        var service = CreateService(persistence, runtime);

        var result = await service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", true)),
            TestContext.Current.CancellationToken);

        Assert.Equal(PlcTaskBindingSaveApplyState.WaitingForConnection, result.State);
        Assert.Equal(["Task.MG1"], result.EnabledTaskKeys);
        Assert.Equal(0, runtime.RestoreCalls);
        Assert.Equal(0, persistence.RestoreCalls);
    }

    [Fact]
    public async Task SaveAndApply_ConcurrentForSamePlc_ShouldSerializeWholeCommand()
    {
        var persistence = new ControlledPersistenceTransaction
        {
            BlockCommitForDeviceId = 1
        };
        var runtime = new ControlledRuntimeTransaction();
        var service = CreateService(persistence, runtime);

        var first = service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", true)),
            TestContext.Current.CancellationToken);
        await persistence.BlockedCommitStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        var second = service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", false)),
            TestContext.Current.CancellationToken);

        Assert.False(second.IsCompleted);
        Assert.Equal([1], persistence.PreparedDeviceIds);

        persistence.AllowBlockedCommit.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 1], persistence.PreparedDeviceIds);
        Assert.Equal(2, runtime.ApplyCalls);
    }

    [Fact]
    public async Task SaveAndApply_WhenSharedHardwareMutationGateIsHeld_ShouldWaitBeforeSnapshotAndCommit()
    {
        var gate = new PlcRuntimeConfigurationMutationGate();
        using var hardwareMutation = await gate.EnterAsync(
            1,
            TestContext.Current.CancellationToken);
        var persistence = new ControlledPersistenceTransaction();
        var runtime = new ControlledRuntimeTransaction();
        var service = CreateService(persistence, runtime, gate);

        var save = service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", true)),
            TestContext.Current.CancellationToken);

        Assert.False(save.IsCompleted);
        Assert.Empty(persistence.PreparedDeviceIds);
        Assert.Equal(0, runtime.CaptureCalls);
        Assert.Equal(0, runtime.ApplyCalls);

        hardwareMutation.Dispose();
        var result = await save.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PlcTaskBindingSaveApplyState.Applied, result.State);
        Assert.Equal([1], persistence.PreparedDeviceIds);
        Assert.Equal(1, runtime.CaptureCalls);
        Assert.Equal(1, runtime.ApplyCalls);
    }

    [Fact]
    public async Task SaveAndApply_ConcurrentForDifferentPlcs_ShouldRemainIndependent()
    {
        var persistence = new ControlledPersistenceTransaction
        {
            BlockCommitForDeviceId = 1
        };
        var runtime = new ControlledRuntimeTransaction();
        var service = CreateService(persistence, runtime);

        var blocked = service.SaveAndApplyAsync(
            1,
            "TestModule",
            States(("Task.MG1", true)),
            TestContext.Current.CancellationToken);
        await persistence.BlockedCommitStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        var independent = service.SaveAndApplyAsync(
            2,
            "TestModule",
            States(("Task.MG2", true)),
            TestContext.Current.CancellationToken);

        var result = await independent.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(PlcTaskBindingSaveApplyState.Applied, result.State);
        Assert.False(blocked.IsCompleted);

        persistence.AllowBlockedCommit.TrySetResult();
        await blocked.WaitAsync(TestContext.Current.CancellationToken);
    }

    private static PlcTaskBindingTransactionService CreateService(
        ControlledPersistenceTransaction persistence,
        ControlledRuntimeTransaction runtime,
        IPlcRuntimeConfigurationMutationGate? runtimeConfigurationMutationGate = null,
        IClientPermissionService? permissionService = null)
        => new(
            persistence,
            runtime,
            runtimeConfigurationMutationGate ?? new PlcRuntimeConfigurationMutationGate(),
            permissionService ?? new ControlledPermissionService(canEditHardware: true),
            new FakeLogService());

    private static IReadOnlyDictionary<string, bool> States(
        params (string Key, bool Enabled)[] states)
        => states.ToDictionary(
            static state => state.Key,
            static state => state.Enabled,
            StringComparer.OrdinalIgnoreCase);

    private sealed class ControlledPermissionService(bool canEditHardware)
        : IClientPermissionService
    {
        public bool CanEditParams => false;

        public bool CanEditHardware { get; set; } = canEditHardware;

        public bool IsLocalAdmin => false;

        public bool HasPermission(string permission)
            => string.Equals(
                   permission,
                   Permissions.HardwareConfig,
                   StringComparison.OrdinalIgnoreCase)
               && CanEditHardware;

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class ControlledPersistenceTransaction
        : IPlcTaskBindingPersistenceTransaction
    {
        private readonly object _stateLock = new();

        public Exception? CommitFailure { get; init; }

        public Exception? RestoreFailure { get; init; }

        public int? BlockCommitForDeviceId { get; init; }

        public TaskCompletionSource BlockedCommitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowBlockedCommit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<int> PreparedDeviceIds { get; } = [];

        public ConcurrentQueue<TransactionEvent> Events { get; } = [];

        public int RestoreCalls { get; private set; }

        public Task<PlcTaskBindingSavePreparation> PrepareAsync(
            int networkDeviceId,
            string moduleId,
            IReadOnlyDictionary<string, bool> taskStates,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_stateLock)
            {
                PreparedDeviceIds.Add(networkDeviceId);
            }

            Record($"prepare:{networkDeviceId}");
            var taskKeys = taskStates.Keys
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(new PlcTaskBindingSavePreparation(
                networkDeviceId,
                $"PLC-{networkDeviceId}",
                $"Device-{networkDeviceId}",
                moduleId,
                taskKeys,
                taskStates,
                taskKeys.Select((key, index) => new PlcTaskBindingRowSnapshot(
                        index + 1,
                        key,
                        Enabled: false,
                        DateTimeOffset.UnixEpoch))
                    .ToArray(),
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                []));
        }

        public async Task CommitAsync(
            PlcTaskBindingSavePreparation preparation,
            CancellationToken cancellationToken = default)
        {
            Record($"commit:{preparation.NetworkDeviceId}");
            if (CommitFailure is not null)
            {
                throw CommitFailure;
            }

            if (BlockCommitForDeviceId == preparation.NetworkDeviceId)
            {
                BlockedCommitStarted.TrySetResult();
                await AllowBlockedCommit.Task.WaitAsync(cancellationToken);
            }
        }

        public Task RestoreAsync(
            PlcTaskBindingSavePreparation preparation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCalls++;
            Record($"restore:{preparation.NetworkDeviceId}");
            return RestoreFailure is null
                ? Task.CompletedTask
                : Task.FromException(RestoreFailure);
        }

        private void Record(string name)
            => Events.Enqueue(new TransactionEvent(
                Interlocked.Increment(ref _eventSequence),
                name));
    }

    private sealed class ControlledRuntimeTransaction
        : IPlcTaskBindingRuntimeTransaction
    {
        public Exception? ApplyFailure { get; init; }

        public Exception? RestoreFailure { get; init; }

        public PlcRuntimeTaskApplyState ApplyState { get; init; }
            = PlcRuntimeTaskApplyState.Applied;

        public int CaptureCalls { get; private set; }

        public int ApplyCalls { get; private set; }

        public int RestoreCalls { get; private set; }

        public ConcurrentQueue<TransactionEvent> Events { get; } = [];

        public PlcRuntimeTaskPlan Capture(
            int networkDeviceId,
            string plcCode,
            string deviceName)
        {
            CaptureCalls++;
            return PlcRuntimeTaskPlan.Empty(networkDeviceId, plcCode, deviceName);
        }

        public Task<PlcRuntimeTaskApplyResult> ApplyCurrentBindingsAsync(
            int networkDeviceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            Record($"runtime-apply:{networkDeviceId}");
            return ApplyFailure is null
                ? Task.FromResult(new PlcRuntimeTaskApplyResult(
                    ApplyState,
                    networkDeviceId == 1 ? ["Task.MG1"] : ["Task.MG2"]))
                : Task.FromException<PlcRuntimeTaskApplyResult>(ApplyFailure);
        }

        public Task RestoreAsync(
            PlcRuntimeTaskPlan snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreCalls++;
            Record($"runtime-restore:{snapshot.NetworkDeviceId}");
            return RestoreFailure is null
                ? Task.CompletedTask
                : Task.FromException(RestoreFailure);
        }

        private void Record(string name)
            => Events.Enqueue(new TransactionEvent(
                Interlocked.Increment(ref _eventSequence),
                name));
    }

    private sealed record TransactionEvent(long Sequence, string Name);
}
