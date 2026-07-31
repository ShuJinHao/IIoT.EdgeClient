using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

namespace IIoT.Edge.Application.Tests;

public sealed class PlcTaskRuntimeStatusBehaviorTests
{
    [Fact]
    public void Store_ShouldUseCaseInsensitivePlcCodeAndTaskKey()
    {
        var store = new PlcTaskRuntimeStatusStore();

        store.SetState(
            "P1-AP01",
            "Task.MG1",
            PlcTaskRuntimeState.Running);

        var snapshot = store.GetSnapshot("p1-ap01", "task.mg1");
        Assert.NotNull(snapshot);
        Assert.Equal("P1-AP01", snapshot!.PlcCode);
        Assert.Equal("Task.MG1", snapshot.TaskKey);
        Assert.Equal(PlcTaskRuntimeState.Running, snapshot.State);
        Assert.NotNull(snapshot.LastSuccessfulAtUtc);
    }

    [Fact]
    public void Store_LastSuccessfulAtUtc_ShouldAdvanceOnlyWhenTaskEntersRunning()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var store = new PlcTaskRuntimeStatusStore(timeProvider);
        var targetedEvents = new List<PlcTaskRuntimeSnapshot>();
        store.StatusChanged += (_, args) =>
        {
            if (string.Equals(args.PlcCode, "P1-AP01", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args.TaskKey, "Task.MG1", StringComparison.OrdinalIgnoreCase)
                && args.Snapshot is not null)
            {
                targetedEvents.Add(args.Snapshot);
            }
        };

        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.WaitingForConnection);
        Assert.Null(store.GetSnapshot("p1-ap01", "task.mg1")?.LastSuccessfulAtUtc);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.Starting);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.Running);
        var firstSuccess = store.GetSnapshot("P1-AP01", "Task.MG1")!.LastSuccessfulAtUtc;

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState(
            "P1-AP01",
            "Task.MG1",
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskFault,
            nameof(InvalidOperationException));
        Assert.Equal(
            firstSuccess,
            store.GetSnapshot("P1-AP01", "Task.MG1")?.LastSuccessfulAtUtc);

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.WaitingForConnection);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.Starting);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.Running);
        var recovered = store.GetSnapshot("p1-ap01", "task.mg1");

        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(2), firstSuccess);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(6), recovered?.LastSuccessfulAtUtc);
        Assert.Equal(recovered, targetedEvents[^1]);
        Assert.All(
            targetedEvents.Skip(3).Take(3),
            snapshot => Assert.Equal(firstSuccess, snapshot.LastSuccessfulAtUtc));
    }

    [Fact]
    public void Store_WhenStateChanges_ShouldPublishOnlyStableSnapshotData()
    {
        var store = new PlcTaskRuntimeStatusStore();
        PlcTaskRuntimeStatusChangedEventArgs? change = null;
        store.StatusChanged += (_, args) => change = args;

        store.SetState(
            "P1-AP01",
            "Task.MG1",
            PlcTaskRuntimeState.Faulted,
            PlcTaskRuntimeErrorCodes.TaskFault,
            nameof(InvalidOperationException));

        Assert.NotNull(change?.Snapshot);
        Assert.Equal(PlcTaskRuntimeErrorCodes.TaskFault, change!.Snapshot!.ErrorCode);
        Assert.Equal(nameof(InvalidOperationException), change.Snapshot.ExceptionType);
        Assert.Throws<ArgumentException>(() =>
            store.SetState(
                "P1-AP01",
                "Task.MG1",
                PlcTaskRuntimeState.Faulted,
                "raw exception message"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.SetState(
                "P1-AP01",
                "Task.MG1",
                (PlcTaskRuntimeState)int.MaxValue));
    }

    [Fact]
    public void Store_WhenTaskOrRuntimeIsSafelyReleased_ShouldPublishRemoval()
    {
        var store = new PlcTaskRuntimeStatusStore();
        var removedKeys = new List<string>();
        store.StatusChanged += (_, args) =>
        {
            if (args.Snapshot is null)
            {
                removedKeys.Add(args.TaskKey);
            }
        };
        store.SetState("P1-AP01", "Task.MG1", PlcTaskRuntimeState.Running);
        store.SetState("P1-AP01", "Task.MG2", PlcTaskRuntimeState.Running);

        store.Remove("p1-ap01", "task.mg1");
        store.RemoveAll("P1-AP01");

        Assert.Equal(["Task.MG1", "Task.MG2"], removedKeys);
        Assert.Empty(store.GetSnapshots("P1-AP01"));
    }

    [Theory]
    [InlineData(false, true, true, true, PlcTaskRuntimeState.Running, PlcTaskBindingDisplayState.BindingMissing)]
    [InlineData(true, false, true, true, PlcTaskRuntimeState.Running, PlcTaskBindingDisplayState.Disabled)]
    [InlineData(true, true, false, true, PlcTaskRuntimeState.Running, PlcTaskBindingDisplayState.Disabled)]
    [InlineData(true, true, true, false, PlcTaskRuntimeState.Running, PlcTaskBindingDisplayState.ConfigurationInvalid)]
    [InlineData(true, true, true, true, null, PlcTaskBindingDisplayState.WaitingForRuntime)]
    [InlineData(true, true, true, true, PlcTaskRuntimeState.WaitingForConnection, PlcTaskBindingDisplayState.WaitingForConnection)]
    [InlineData(true, true, true, true, PlcTaskRuntimeState.Faulted, PlcTaskBindingDisplayState.Faulted)]
    public void DisplayState_ShouldApplyConfigurationBeforeRuntimePrecedence(
        bool hasSavedBinding,
        bool isDeviceEnabled,
        bool isTaskEnabled,
        bool canRun,
        PlcTaskRuntimeState? runtimeState,
        PlcTaskBindingDisplayState expected)
    {
        var actual = PlcTaskBindingDisplayStateResolver.Resolve(
            hasSavedBinding,
            isDeviceEnabled,
            isTaskEnabled,
            canRun,
            runtimeState);

        Assert.Equal(expected, actual);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
