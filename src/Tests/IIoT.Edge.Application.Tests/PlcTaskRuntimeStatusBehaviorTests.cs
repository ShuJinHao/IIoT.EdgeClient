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
}
