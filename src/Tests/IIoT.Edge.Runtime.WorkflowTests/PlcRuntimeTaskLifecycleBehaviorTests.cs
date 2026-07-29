using System.Collections.Concurrent;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Testing;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class PlcRuntimeTaskLifecycleBehaviorTests
{
    [Fact]
    public async Task StartBeforeConnection_ShouldOnlyStartConnectionAndDeferBusinessCreation()
    {
        var connection = new ControlledLoopTask("Connection");
        var periodicRead = new ControlledLoopTask("PeriodicRead");
        var factoryCalls = new AsyncCounter();
        var business = new ControlledBusinessTask("Task.MG1");
        var runtime = CreateRuntime(connection, periodicRead);
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) =>
                {
                    factoryCalls.Increment();
                    return business;
                })),
            TestContext.Current.CancellationToken);

        runtime.Start();
        await connection.Starts.WaitForAtLeastAsync(
            1,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, periodicRead.Starts.Count);
        Assert.Equal(0, factoryCalls.Count);
        Assert.Equal(0, business.Starts.Count);

        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task ConnectionTransitions_ShouldPauseAndResumeSameTaskInstancesOnce()
    {
        var connection = new ControlledLoopTask("Connection");
        var periodicRead = new ControlledLoopTask("PeriodicRead");
        var mg1 = new ControlledBusinessTask("Task.MG1");
        var mg2 = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(connection, periodicRead);
        var originalBuffer = runtime.Buffer;
        var originalContext = runtime.Context;
        var originalService = runtime.PlcService;
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => mg1),
                ("Task.MG2", (_, _) => mg2)),
            TestContext.Current.CancellationToken);

        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            periodicRead.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg1.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        var originalMg1 = runtime.GetBusinessTask("Task.MG1");
        var originalMg2 = runtime.GetBusinessTask("Task.MG2");
        Assert.Equal(5, runtime.GetRunningHandlesSnapshot().Count);

        runtime.ConnectionSignal.Report(true);
        Assert.Equal(1, periodicRead.Starts.Count);
        Assert.Equal(1, mg1.Starts.Count);
        Assert.Equal(1, mg2.Starts.Count);

        runtime.ConnectionSignal.Report(false);
        await Task.WhenAll(
            periodicRead.Stops.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg1.Stops.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg2.Stops.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            periodicRead.Starts.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken),
            mg1.Starts.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken));

        Assert.Same(originalMg1, runtime.GetBusinessTask("Task.MG1"));
        Assert.Same(originalMg2, runtime.GetBusinessTask("Task.MG2"));
        Assert.Same(originalBuffer, runtime.Buffer);
        Assert.Same(originalContext, runtime.Context);
        Assert.Same(originalService, runtime.PlcService);
        Assert.Equal(1, connection.Starts.Count);
        Assert.Equal(5, runtime.GetRunningHandlesSnapshot().Count);

        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task ApplyPlan_WhenOneTaskChanges_ShouldKeepOtherTaskAndBaseRuntimeReferences()
    {
        var connection = new ControlledLoopTask("Connection");
        var periodicRead = new ControlledLoopTask("PeriodicRead");
        var mg1Instances = new ConcurrentQueue<ControlledBusinessTask>();
        var mg1Creations = new AsyncCounter();
        var mg2 = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(connection, periodicRead);
        var fullPlan = CreatePlan(
            runtime.DeviceName,
            ("Task.MG1", (_, _) =>
            {
                var task = new ControlledBusinessTask("Task.MG1");
                mg1Instances.Enqueue(task);
                mg1Creations.Increment();
                return task;
            }),
            ("Task.MG2", (_, _) => mg2));
        await runtime.ApplyTaskPlanAsync(fullPlan, TestContext.Current.CancellationToken);
        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            mg1Creations.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));

        var originalConnection = runtime.ConnectionTask;
        var originalPeriodicRead = runtime.PeriodicReadTask;
        var originalBuffer = runtime.Buffer;
        var originalContext = runtime.Context;
        var originalService = runtime.PlcService;
        var originalMg2 = runtime.GetBusinessTask("Task.MG2");
        Assert.True(mg1Instances.TryPeek(out var firstMg1));

        var disableResult = await runtime.ApplyTaskPlanAsync(
            CreatePlan(runtime.DeviceName, ("Task.MG2", (_, _) => mg2)),
            TestContext.Current.CancellationToken);

        Assert.Equal(PlcRuntimeTaskApplyState.Applied, disableResult.State);
        Assert.Null(runtime.GetBusinessTask("Task.MG1"));
        Assert.Same(originalMg2, runtime.GetBusinessTask("Task.MG2"));
        Assert.Equal(1, firstMg1!.Stops.Count);
        Assert.Equal(1, mg2.Starts.Count);
        Assert.Equal(0, mg2.Stops.Count);

        await runtime.ApplyTaskPlanAsync(fullPlan, TestContext.Current.CancellationToken);
        await mg1Creations.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken);
        Assert.Equal(2, mg1Instances.Count);
        Assert.Same(originalMg2, runtime.GetBusinessTask("Task.MG2"));
        Assert.Same(originalConnection, runtime.ConnectionTask);
        Assert.Same(originalPeriodicRead, runtime.PeriodicReadTask);
        Assert.Same(originalBuffer, runtime.Buffer);
        Assert.Same(originalContext, runtime.Context);
        Assert.Same(originalService, runtime.PlcService);
        Assert.Equal(1, connection.Starts.Count);
        Assert.Equal(1, periodicRead.Starts.Count);

        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task ConnectionStart_WhenOneBusinessStartupFails_ShouldStillStartOtherTask()
    {
        var failed = new ControlledBusinessTask("Task.MG1")
        {
            FailStartup = true
        };
        var healthy = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(
            new ControlledLoopTask("Connection"),
            new ControlledLoopTask("PeriodicRead"));
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => failed),
                ("Task.MG2", (_, _) => healthy)),
            TestContext.Current.CancellationToken);

        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            failed.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            healthy.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(1, failed.Starts.Count);
        Assert.Equal(1, healthy.Starts.Count);
        Assert.Contains(
            runtime.LoggerEntries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains("Task.MG1", StringComparison.Ordinal)
                     && entry.Message.Contains("仅隔离该 TaskKey", StringComparison.Ordinal));
        Assert.DoesNotContain(
            runtime.LoggerEntries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains("Task.MG2", StringComparison.Ordinal));

        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task ApplyPlan_WhenTargetStopFails_ShouldLeaveOtherTaskAndConnectionUntouched()
    {
        var connection = new ControlledLoopTask("Connection");
        var periodicRead = new ControlledLoopTask("PeriodicRead");
        var mg1 = new ControlledBusinessTask("Task.MG1");
        var mg2 = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(connection, periodicRead);
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => mg1),
                ("Task.MG2", (_, _) => mg2)),
            TestContext.Current.CancellationToken);
        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            mg1.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        var originalMg2 = runtime.GetBusinessTask("Task.MG2");
        mg1.ThrowOnStop = true;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.ApplyTaskPlanAsync(
                CreatePlan(runtime.DeviceName, ("Task.MG2", (_, _) => mg2)),
                TestContext.Current.CancellationToken));

        Assert.Contains("Task.MG1", error.Message, StringComparison.Ordinal);
        Assert.Same(originalMg2, runtime.GetBusinessTask("Task.MG2"));
        Assert.Equal(1, connection.Starts.Count);
        Assert.Equal(1, periodicRead.Starts.Count);
        Assert.Equal(1, mg2.Starts.Count);
        Assert.Equal(0, mg2.Stops.Count);

        mg1.ThrowOnStop = false;
        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task ApplyPlan_WhenLaterStopFails_ShouldRestorePreviouslyStoppedTasks()
    {
        var mg1 = new ControlledBusinessTask("Task.MG1");
        var mg2 = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(
            new ControlledLoopTask("Connection"),
            new ControlledLoopTask("PeriodicRead"));
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => mg1),
                ("Task.MG2", (_, _) => mg2)),
            TestContext.Current.CancellationToken);
        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            mg1.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        var originalMg1 = runtime.GetBusinessTask("Task.MG1");
        var originalMg2 = runtime.GetBusinessTask("Task.MG2");
        mg2.ThrowOnStop = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.ApplyTaskPlanAsync(
                PlcRuntimeTaskPlan.Empty(runtime.DeviceName),
                TestContext.Current.CancellationToken));
        await Task.WhenAll(
            mg1.Starts.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken),
            mg2.Starts.WaitForAtLeastAsync(2, TestContext.Current.CancellationToken));

        Assert.Same(originalMg1, runtime.GetBusinessTask("Task.MG1"));
        Assert.Same(originalMg2, runtime.GetBusinessTask("Task.MG2"));
        Assert.Equal(["Task.MG1", "Task.MG2"], runtime.EnabledTaskKeys);
        Assert.Equal(1, mg1.Stops.Count);
        Assert.Equal(1, mg2.Stops.Count);

        mg2.ThrowOnStop = false;
        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task BusinessExecutionFaultAfterStartup_ShouldBeObservedForThatTaskKey()
    {
        var faulting = new FaultingAfterStartupBusinessTask("Task.MG1");
        var healthy = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(
            new ControlledLoopTask("Connection"),
            new ControlledLoopTask("PeriodicRead"));
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => faulting),
                ("Task.MG2", (_, _) => healthy)),
            TestContext.Current.CancellationToken);
        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            faulting.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            healthy.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        var observed = runtime.WaitForLogAsync(
            entry => entry.Level == "Error"
                     && entry.Message.Contains("Task.MG1", StringComparison.Ordinal)
                     && entry.Message.Contains("执行故障", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        faulting.FailExecution();
        await observed;

        Assert.Equal(1, healthy.Starts.Count);
        Assert.Equal(0, healthy.Stops.Count);
        Assert.DoesNotContain(
            runtime.LoggerEntries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains("Task.MG2", StringComparison.Ordinal));

        await CleanupAsync(runtime);
    }

    [Fact]
    public async Task BusinessExecutionEndingBeforeStartup_ShouldFailWithoutBlockingOtherTask()
    {
        var earlyExit = new ExecutionBeforeStartupBusinessTask("Task.MG1");
        var healthy = new ControlledBusinessTask("Task.MG2");
        var runtime = CreateRuntime(
            new ControlledLoopTask("Connection"),
            new ControlledLoopTask("PeriodicRead"));
        await runtime.ApplyTaskPlanAsync(
            CreatePlan(
                runtime.DeviceName,
                ("Task.MG1", (_, _) => earlyExit),
                ("Task.MG2", (_, _) => healthy)),
            TestContext.Current.CancellationToken);
        var failureLogged = runtime.WaitForLogAsync(
            entry => entry.Level == "Error"
                     && entry.Message.Contains("Task.MG1", StringComparison.Ordinal)
                     && entry.Message.Contains("启动失败", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await Task.WhenAll(
            failureLogged,
            healthy.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));

        Assert.Equal(1, earlyExit.Starts.Count);
        Assert.Equal(1, healthy.Starts.Count);

        await CleanupAsync(runtime);
    }

    [Fact]
    public void SingleTaskFactory_ShouldFailClosedForZeroDuplicateAndMismatchedResults()
    {
        IReadOnlySet<string>? requestedKeys = null;
        var expected = new ControlledBusinessTask("Task.MG1");
        var actual = PlcRuntimeSingleTaskFactory.CreateRequired(
            "Task.MG1",
            keys =>
            {
                requestedKeys = keys;
                return [expected];
            });
        var zero = Assert.Throws<InvalidOperationException>(() =>
            PlcRuntimeSingleTaskFactory.CreateRequired("Task.MG1", _ => []));
        var duplicate = Assert.Throws<InvalidOperationException>(() =>
            PlcRuntimeSingleTaskFactory.CreateRequired(
                "Task.MG1",
                _ =>
                [
                    new ControlledBusinessTask("Task.MG1"),
                    new ControlledBusinessTask("Task.MG1")
                ]));
        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            PlcRuntimeSingleTaskFactory.CreateRequired(
                "Task.MG1",
                _ => [new ControlledBusinessTask("Task.MG2")]));

        Assert.Same(expected, actual);
        Assert.Equal(["Task.MG1"], requestedKeys);
        Assert.Contains("返回 0 个任务", zero.Message, StringComparison.Ordinal);
        Assert.Contains("返回 2 个任务", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("返回任务名 Task.MG2", mismatch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskController_WhenApplyingOnePlc_ShouldNotChangeOtherPlc()
    {
        var registry = new PlcRuntimeRegistry();
        var controller = new PlcRuntimeTaskController(registry);
        var plcAConnection = new ControlledLoopTask("PLC-A.Connection");
        var plcBConnection = new ControlledLoopTask("PLC-B.Connection");
        var plcAPeriodic = new ControlledLoopTask("PLC-A.Periodic");
        var plcBPeriodic = new ControlledLoopTask("PLC-B.Periodic");
        var plcAMg1 = new ControlledBusinessTask("Task.MG1");
        var plcBMg1 = new ControlledBusinessTask("Task.MG1");
        var plcA = CreateRuntime(plcAConnection, plcAPeriodic, "PLC-A", 1);
        var plcB = CreateRuntime(plcBConnection, plcBPeriodic, "PLC-B", 2);
        Assert.True(registry.TryAddRuntime(plcA));
        Assert.True(registry.TryAddRuntime(plcB));
        await plcA.ApplyTaskPlanAsync(
            CreatePlan(plcA.DeviceName, ("Task.MG1", (_, _) => plcAMg1)),
            TestContext.Current.CancellationToken);
        await plcB.ApplyTaskPlanAsync(
            CreatePlan(plcB.DeviceName, ("Task.MG1", (_, _) => plcBMg1)),
            TestContext.Current.CancellationToken);
        plcA.Start();
        plcB.Start();
        plcA.ConnectionSignal.Report(true);
        plcB.ConnectionSignal.Report(true);
        await Task.WhenAll(
            plcAMg1.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken),
            plcBMg1.Starts.WaitForAtLeastAsync(1, TestContext.Current.CancellationToken));
        var plcBTask = plcB.GetBusinessTask("Task.MG1");
        var plcBBuffer = plcB.Buffer;
        var plcBService = plcB.PlcService;

        var result = await controller.RegisterAndApplyAsync(
            PlcRuntimeTaskPlan.Empty(plcA.DeviceName),
            TestContext.Current.CancellationToken);

        Assert.Equal(PlcRuntimeTaskApplyState.Applied, result.State);
        Assert.Null(plcA.GetBusinessTask("Task.MG1"));
        Assert.Same(plcBTask, plcB.GetBusinessTask("Task.MG1"));
        Assert.Same(plcBBuffer, plcB.Buffer);
        Assert.Same(plcBService, plcB.PlcService);
        Assert.Equal(1, plcBConnection.Starts.Count);
        Assert.Equal(1, plcBPeriodic.Starts.Count);
        Assert.Equal(1, plcBMg1.Starts.Count);
        Assert.Equal(0, plcBMg1.Stops.Count);

        await CleanupAsync(plcA);
        await CleanupAsync(plcB);
    }

    [Fact]
    public async Task TaskController_WhenRuntimeApplyFails_ShouldKeepPreviousRegisteredPlan()
    {
        var registry = new PlcRuntimeRegistry();
        var controller = new PlcRuntimeTaskController(registry);
        var businessTask = new ControlledBusinessTask("Task.MG1");
        var runtime = CreateRuntime(
            new ControlledLoopTask("Connection"),
            new ControlledLoopTask("PeriodicRead"));
        var originalPlan = CreatePlan(
            runtime.DeviceName,
            ("Task.MG1", (_, _) => businessTask));
        registry.RegisterTaskPlan(originalPlan);
        Assert.True(registry.TryAddRuntime(runtime));
        await runtime.ApplyTaskPlanAsync(
            originalPlan,
            TestContext.Current.CancellationToken);
        runtime.Start();
        runtime.ConnectionSignal.Report(true);
        await businessTask.Starts.WaitForAtLeastAsync(
            1,
            TestContext.Current.CancellationToken);
        businessTask.ThrowOnStop = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.RegisterAndApplyAsync(
                PlcRuntimeTaskPlan.Empty(runtime.DeviceName),
                TestContext.Current.CancellationToken));

        Assert.Equal(["Task.MG1"], registry.GetTaskPlan(runtime.DeviceName).TaskKeys);
        Assert.Equal(["Task.MG1"], runtime.EnabledTaskKeys);

        businessTask.ThrowOnStop = false;
        await CleanupAsync(runtime);
    }

    private static TestPlcDeviceRuntimeHandle CreateRuntime(
        ControlledLoopTask connection,
        ControlledLoopTask periodicRead,
        string deviceName = "PLC-A",
        int deviceId = 1)
    {
        var logger = new ConcurrentLogService();
        return new TestPlcDeviceRuntimeHandle(
            logger,
            new PlcDeviceRuntimeHandle
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                PlcService = new InertPlcService(),
                Buffer = new PlcBuffer(0, 0),
                Context = new ProductionContext
                {
                    DeviceName = deviceName,
                    NetworkDeviceId = deviceId
                },
                ConnectionTask = connection,
                PeriodicReadTask = periodicRead,
                ConnectionSignal = new PlcRuntimeConnectionSignal(),
                Logger = logger,
                StatusStore = new PlcConnectionStatusStore(),
                CancellationTokenSource = new CancellationTokenSource()
            });
    }

    private static PlcRuntimeTaskPlan CreatePlan(
        string deviceName,
        params (string TaskKey, PlcRuntimeBusinessTaskFactory Factory)[] tasks)
        => new(
            deviceName,
            tasks.Select(
                static task =>
                    new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                        task.TaskKey,
                        task.Factory)));

    private static async Task CleanupAsync(PlcDeviceRuntimeHandle runtime)
    {
        await runtime.RequestStopAsync();
        await Task.WhenAll(runtime.GetRunningHandlesSnapshot());
        await runtime.PlcService.DisposeAsync();
        runtime.DisposeCancellation();
    }

    private sealed class TestPlcDeviceRuntimeHandle(
        ConcurrentLogService logger,
        PlcDeviceRuntimeHandle runtime)
    {
        public static implicit operator PlcDeviceRuntimeHandle(
            TestPlcDeviceRuntimeHandle value)
            => value.Runtime;

        public PlcDeviceRuntimeHandle Runtime { get; } = runtime;

        public IReadOnlyCollection<LogEntry> LoggerEntries => logger.Entries;

        public string DeviceName => Runtime.DeviceName;
        public IPlcService PlcService => Runtime.PlcService;
        public PlcBuffer Buffer => (PlcBuffer)Runtime.Buffer;
        public ProductionContext Context => Runtime.Context;
        public IPlcTask ConnectionTask => Runtime.ConnectionTask;
        public IPlcTask PeriodicReadTask => Runtime.PeriodicReadTask;
        public PlcRuntimeConnectionSignal ConnectionSignal => Runtime.ConnectionSignal;
        public IReadOnlyCollection<string> EnabledTaskKeys => Runtime.EnabledTaskKeys;

        public void Start() => Runtime.Start();

        public Task<PlcRuntimeTaskApplyResult> ApplyTaskPlanAsync(
            PlcRuntimeTaskPlan plan,
            CancellationToken cancellationToken)
            => Runtime.ApplyTaskPlanAsync(plan, cancellationToken);

        public IPlcTask? GetBusinessTask(string taskKey)
            => Runtime.GetBusinessTask(taskKey);

        public IReadOnlyCollection<Task> GetRunningHandlesSnapshot()
            => Runtime.GetRunningHandlesSnapshot();

        public Task WaitForLogAsync(
            Func<LogEntry, bool> predicate,
            CancellationToken cancellationToken)
            => logger.WaitForAsync(predicate, cancellationToken);
    }

    private sealed class ControlledLoopTask(string taskName) : IPlcTask
    {
        public string TaskName { get; } = taskName;

        public AsyncCounter Starts { get; } = new();

        public AsyncCounter Stops { get; } = new();

        public Task StartAsync(CancellationToken ct)
        {
            Starts.Increment();
            return RunAsync(ct);
        }

        public Task StopAsync(CancellationToken ct)
        {
            Stops.Increment();
            return Task.CompletedTask;
        }

        private static async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private sealed class ControlledBusinessTask(string taskName)
        : IPlcTask, IStartupAwareBackgroundTask
    {
        public string TaskName { get; } = taskName;

        public AsyncCounter Starts { get; } = new();

        public AsyncCounter Stops { get; } = new();

        public bool FailStartup { get; init; }

        public bool ThrowOnStop { get; set; }

        public Task StartAsync(CancellationToken ct)
            => StartWithStartup(ct).Execution;

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            Starts.Increment();
            var execution = RunAsync(cancellationToken);
            var startup = FailStartup
                ? Task.FromException(
                    new InvalidOperationException($"{TaskName} startup failed."))
                : Task.CompletedTask;
            return new BackgroundTaskRun(startup, execution);
        }

        public Task StopAsync(CancellationToken ct)
        {
            Stops.Increment();
            return ThrowOnStop
                ? Task.FromException(
                    new InvalidOperationException($"{TaskName} stop failed."))
                : Task.CompletedTask;
        }

        private static async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private sealed class FaultingAfterStartupBusinessTask(string taskName)
        : IPlcTask, IStartupAwareBackgroundTask
    {
        private readonly TaskCompletionSource _failExecution =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string TaskName { get; } = taskName;

        public AsyncCounter Starts { get; } = new();

        public void FailExecution()
            => _failExecution.TrySetResult();

        public Task StartAsync(CancellationToken ct)
            => StartWithStartup(ct).Execution;

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            Starts.Increment();
            return new BackgroundTaskRun(
                Task.CompletedTask,
                RunAsync(cancellationToken));
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            await _failExecution.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException($"{TaskName} execution failed.");
        }
    }

    private sealed class ExecutionBeforeStartupBusinessTask(string taskName)
        : IPlcTask, IStartupAwareBackgroundTask
    {
        public string TaskName { get; } = taskName;

        public AsyncCounter Starts { get; } = new();

        public Task StartAsync(CancellationToken ct)
            => StartWithStartup(ct).Execution;

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
        {
            Starts.Increment();
            return new BackgroundTaskRun(
                new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task,
                Task.FromException(
                    new InvalidOperationException(
                        $"{TaskName} execution ended before startup.")));
        }
    }

    private sealed class AsyncCounter
    {
        private readonly object _sync = new();
        private readonly List<(int Expected, TaskCompletionSource Completion)> _waiters = [];
        private int _count;

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _count;
                }
            }
        }

        public void Increment()
        {
            TaskCompletionSource[] completed;
            lock (_sync)
            {
                _count++;
                completed = _waiters
                    .Where(waiter => _count >= waiter.Expected)
                    .Select(static waiter => waiter.Completion)
                    .ToArray();
                _waiters.RemoveAll(waiter => _count >= waiter.Expected);
            }

            foreach (var completion in completed)
            {
                completion.TrySetResult();
            }
        }

        public Task WaitForAtLeastAsync(
            int expected,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_count >= expected)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((expected, completion));
                return completion.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class ConcurrentLogService : ILogService
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Add("Debug", message);
        public void Info(string message) => Add("Info", message);
        public void Warn(string message) => Add("Warn", message);
        public void Error(string message) => Add("Error", message);
        public void Fatal(string message) => Add("Fatal", message);

        public async Task WaitForAsync(
            Func<LogEntry, bool> predicate,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            if (_entries.Any(predicate))
            {
                return;
            }

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleEntry(LogEntry entry)
            {
                if (predicate(entry))
                {
                    completion.TrySetResult();
                }
            }

            EntryAdded += HandleEntry;
            try
            {
                if (_entries.Any(predicate))
                {
                    return;
                }

                await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                EntryAdded -= HandleEntry;
            }
        }

        private void Add(string level, string message)
        {
            var entry = new LogEntry
            {
                Level = level,
                Message = message,
                Time = DateTime.UtcNow
            };
            _entries.Enqueue(entry);
            EntryAdded?.Invoke(entry);
        }
    }

    private sealed class InertPlcService : PlcServiceTestDouble;
}
