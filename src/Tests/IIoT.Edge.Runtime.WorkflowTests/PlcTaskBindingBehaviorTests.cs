using System.Linq.Expressions;
using System.Collections.Concurrent;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Factory;
using IIoT.Edge.Module.Contracts.Plc.Signals;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using IIoT.Edge.Module.Contracts.Identity;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class PlcTaskBindingBehaviorTests
{
    [Fact]
    public async Task GetEnabledTaskKeys_WhenGlobalDefaultEnabled_ShouldStillDisableMissingRows()
    {
        var service = CreateService(defaultEnableAllTasks: true);

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenNoConfiguredDefault_ShouldDisableMissingRows()
    {
        var service = CreateService(defaultEnableAllTasks: null);

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenCandidateDefaultEnabled_ShouldStillDisableMissingRow()
    {
        var service = CreateService(defaultEnableAllTasks: null);
        var candidates = new[]
        {
            new TaskCandidate(
                "Task.Default",
                "默认启用任务",
                [new TaskRequiredSignal("Signal.Business", "Read")],
                DefaultEnabled: true)
        };

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            candidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenCandidateDefaultEnabledButIoMissing_ShouldKeepDisabled()
    {
        var service = CreateService(defaultEnableAllTasks: null);
        var candidates = new[]
        {
            new TaskCandidate(
                "Task.Default",
                "默认启用任务",
                [new TaskRequiredSignal("Signal.Missing", "Read")],
                DefaultEnabled: true)
        };

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            candidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenCandidateDefaultEnabledButSavedDisabled_ShouldUseSavedRow()
    {
        var harness = CreateService(defaultEnableAllTasks: null);
        harness.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.Default", enabled: false, DateTimeOffset.UtcNow));
        var candidates = new[]
        {
            new TaskCandidate(
                "Task.Default",
                "默认启用任务",
                [new TaskRequiredSignal("Signal.Business", "Read")],
                DefaultEnabled: true)
        };

        var enabledKeys = await harness.Service.GetEnabledTaskKeysAsync(
            1,
            candidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetEnabledTaskKeys_WhenConfiguredDefaultExists_ShouldIgnoreConfiguredValue(
        bool configuredDefault)
    {
        var service = CreateService(defaultEnableAllTasks: configuredDefault);

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(enabledKeys);
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenSavedRowExists_ShouldOverrideDefault()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        harness.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.A", enabled: false, DateTimeOffset.UtcNow));
        harness.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.B", enabled: true, DateTimeOffset.UtcNow));

        var enabledKeys = await harness.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            AllTestMappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Task.B"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetEnabledTaskKeys_WhenSavedEnabled_ShouldOnlyEnableRunnableTasks()
    {
        var service = CreateService(defaultEnableAllTasks: true);
        service.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.A", enabled: true, DateTimeOffset.UtcNow));
        service.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.B", enabled: true, DateTimeOffset.UtcNow));
        var mappings = AllTestMappings
            .Where(static mapping => !string.Equals(mapping.SignalKey, "Signal.Business", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var enabledKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            mappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Task.A"], enabledKeys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetConfiguredEnabledTaskKeys_WhenSavedTaskLosesSignal_ShouldRetainTaskForDiagnostics()
    {
        var service = CreateService(defaultEnableAllTasks: true);
        service.Bindings.Add(PlcTaskBindingEntity.Create(1, "Task.B", enabled: true, DateTimeOffset.UtcNow));
        var mappings = AllTestMappings
            .Where(static mapping => !string.Equals(
                mapping.SignalKey,
                "Signal.Business",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var configuredKeys = await service.Service.GetConfiguredEnabledTaskKeysAsync(
            1,
            TestCandidates,
            TestContext.Current.CancellationToken);
        var runnableKeys = await service.Service.GetEnabledTaskKeysAsync(
            1,
            TestCandidates,
            mappings,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Task.B"], configuredKeys);
        Assert.Empty(runnableKeys);
    }

    [Fact]
    public void ValidateEnabledTasks_WhenWriteSignalMissing_ShouldReportDirectionSpecificIssue()
    {
        var service = CreateService(defaultEnableAllTasks: true).Service;
        var mappings = new[]
        {
            new ModuleIoSnapshot("Signal.Shared", "D100", 1, "Int16", "Read", 1, "信号交互", "共享信号")
        };

        var result = service.ValidateEnabledTasks(
            TestCandidates,
            new HashSet<string>(["Task.A"], StringComparer.OrdinalIgnoreCase),
            mappings);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("Task.A", issue.TaskKey);
        Assert.Equal("Signal.Shared", issue.RequiredSignal!.SignalKey);
        Assert.Equal("Write", issue.RequiredSignal.Direction);
    }

    [Fact]
    public async Task PrepareAndCommit_WhenHeartbeatDisabled_ShouldPersistCandidateRows()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        var device = NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102);
        device.UpdateDeviceModel(PlcType.S7.ToString());
        harness.NetworkDevices.Add(device);

        await SaveBindingsForTestAsync(
            harness.Service,
            device.Id,
            "TestModule",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Task.A"] = false,
                ["Task.B"] = true
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, harness.Bindings.Items.Count);
        Assert.Contains(harness.Bindings.Items, static x => x.TaskKey == "Task.A" && !x.Enabled);
        Assert.Contains(harness.Bindings.Items, static x => x.TaskKey == "Task.B" && x.Enabled);
    }

    [Fact]
    public async Task PrepareAndCommit_ShouldPreserveRowsOutsideCurrentModuleCandidates()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        var device = harness.NetworkDevices.Add(
            NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102));
        device.UpdateDeviceModel(PlcType.S7.ToString());
        var unrelatedUpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1);
        var unrelated = harness.Bindings.Add(PlcTaskBindingEntity.Create(
            device.Id,
            "OtherModule.Task",
            enabled: true,
            unrelatedUpdatedAt));

        await SaveBindingsForTestAsync(
            harness.Service,
            device.Id,
            "TestModule",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Task.A"] = false,
                ["Task.B"] = true
            },
            TestContext.Current.CancellationToken);

        Assert.Same(unrelated, harness.Bindings.Items.Single(
            static row => row.TaskKey == "OtherModule.Task"));
        Assert.True(unrelated.Enabled);
        Assert.Equal(unrelatedUpdatedAt, unrelated.UpdatedAt);
        Assert.Equal(3, harness.Bindings.Items.Count);
    }

    [Fact]
    public async Task Prepare_WhenSubmittedTaskKeyIsUnknown_ShouldFailWithoutPartialSave()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        var device = harness.NetworkDevices.Add(
            NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102));
        device.UpdateDeviceModel(PlcType.S7.ToString());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveBindingsForTestAsync(
                harness.Service,
                device.Id,
                "TestModule",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Task.A"] = true,
                    ["Task.Unknown"] = true
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("Task.Unknown", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Bindings.Items);
    }

    [Fact]
    public async Task Prepare_WhenDeviceIsNotPlc_ShouldFailWithoutPartialSave()
    {
        var harness = CreateService(defaultEnableAllTasks: true);
        var device = harness.NetworkDevices.Add(
            NetworkDeviceEntity.Create("Camera-A", DeviceType.Camera, "127.0.0.1", 102));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveBindingsForTestAsync(
                harness.Service,
                device.Id,
                "TestModule",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Task.A"] = true
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("不是 PLC", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Bindings.Items);
    }

    [Fact]
    public async Task Prepare_WhenEnabledTaskMissingIo_ShouldFail()
    {
        var harness = CreateService(defaultEnableAllTasks: false, seedIoMappings: false);
        var device = NetworkDeviceEntity.Create("PLC-A", DeviceType.PLC, "127.0.0.1", 102);
        device.UpdateDeviceModel(PlcType.S7.ToString());
        harness.NetworkDevices.Add(device);
        AddTestIoMappings(harness.IoMappings, device.Id, includeBusinessSignal: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveBindingsForTestAsync(
                harness.Service,
                device.Id,
                "TestModule",
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Task.A"] = false,
                    ["Task.B"] = true
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("Signal.Business/Read", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlcDeviceRuntimeBuilder_WhenBuildingTwoPlcs_ShouldCreateIndependentBuffersAndContexts()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var plcA = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6000));
        var plcB = networkDevices.Add(CreateLifecyclePlc("PLC-B", 6001));
        AddTestIoMappings(ioMappings, plcA.Id);
        AddTestIoMappings(ioMappings, plcB.Id);
        var dataStore = new PlcDataStore();
        var contextStore = new FakeProductionContextStore();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            dataStore,
            new TrackingPlcServiceFactory(),
            contextStore,
            new FakeLogService(),
            new PlcConnectionStatusStore(),
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        var factoryCalls =
            new ConcurrentQueue<(string DeviceName, int NetworkDeviceId, IPlcBuffer Buffer, ProductionContext Context)>();
        var plcAFactoryCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plcBFactoryCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        IPlcTask CreateBusinessTask(
            IPlcBuffer buffer,
            ProductionContext context,
            TaskCompletionSource factoryCalled)
        {
            factoryCalls.Enqueue((context.DeviceName, context.NetworkDeviceId, buffer, context));
            factoryCalled.TrySetResult();
            return new NoopPlcTask($"Business.{context.DeviceName}");
        }

        var runtimeA = await runtimeBuilder.BuildAsync(
            plcA,
            new PlcRuntimeTaskPlan(
                plcA.Id,
                plcA.PlcCode,
                plcA.DeviceName,
                [
                    new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                        "Business.PLC-A",
                        (buffer, context) => CreateBusinessTask(buffer, context, plcAFactoryCalled))
                ]),
            TestContext.Current.CancellationToken);
        var runtimeB = await runtimeBuilder.BuildAsync(
            plcB,
            new PlcRuntimeTaskPlan(
                plcB.Id,
                plcB.PlcCode,
                plcB.DeviceName,
                [
                    new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                        "Business.PLC-B",
                        (buffer, context) => CreateBusinessTask(buffer, context, plcBFactoryCalled))
                ]),
            TestContext.Current.CancellationToken);

        Assert.NotSame(dataStore.GetBuffer(plcA.Id), dataStore.GetBuffer(plcB.Id));
        Assert.Empty(factoryCalls);
        Assert.Equal("PlcIoScan_PLC-A", runtimeA.ConnectionTask.TaskName);
        Assert.Equal("PlcDataReadScan_PLC-A", runtimeA.PeriodicReadTask.TaskName);
        Assert.Equal(["Business.PLC-A"], runtimeA.EnabledTaskKeys);
        Assert.Equal("PlcIoScan_PLC-B", runtimeB.ConnectionTask.TaskName);
        Assert.Equal("PlcDataReadScan_PLC-B", runtimeB.PeriodicReadTask.TaskName);
        Assert.Equal(["Business.PLC-B"], runtimeB.EnabledTaskKeys);

        runtimeA.Start();
        runtimeB.Start();
        await Task.WhenAll(plcAFactoryCalled.Task, plcBFactoryCalled.Task)
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Collection(
            factoryCalls.OrderBy(static call => call.DeviceName, StringComparer.OrdinalIgnoreCase),
            call =>
            {
                Assert.Equal("PLC-A", call.DeviceName);
                Assert.Equal(plcA.Id, call.NetworkDeviceId);
                Assert.Same(dataStore.GetBuffer(plcA.Id), call.Buffer);
            },
            call =>
            {
                Assert.Equal("PLC-B", call.DeviceName);
                Assert.Equal(plcB.Id, call.NetworkDeviceId);
                Assert.Same(dataStore.GetBuffer(plcB.Id), call.Buffer);
            });

        await runtimeA.RequestStopAsync();
        await runtimeB.RequestStopAsync();
        await Task.WhenAll(runtimeA.GetRunningHandlesSnapshot());
        await Task.WhenAll(runtimeB.GetRunningHandlesSnapshot());
        await runtimeA.PlcService.DisposeAsync();
        await runtimeB.PlcService.DisposeAsync();
        runtimeA.DisposeCancellation();
        runtimeB.DisposeCancellation();
    }

    [Fact]
    public async Task PlcDeviceRuntimeBuilder_WhenStableContextBlocked_ShouldKeepBaseRuntimeAndIsolateBusinessTasks()
    {
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var device = networkDevices.Add(
            CreateLifecyclePlc(
                "Current-Display",
                6050,
                plcCode: "PLC-STABLE-BLOCK"));
        var logger = new FakeLogService();
        var statusStore = new PlcConnectionStatusStore();
        var plcServiceFactory = new TrackingPlcServiceFactory();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            new BlockedPlcProductionContextStore(),
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        var plan = new PlcRuntimeTaskPlan(
            device.Id,
            device.PlcCode,
            device.DeviceName,
            [
                new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                    "Task.Blocked",
                    (_, _) => new NoopPlcTask("Task.Blocked"))
            ]);

        var runtime = await runtimeBuilder.BuildAsync(
            device,
            plan,
            TestContext.Current.CancellationToken);

        Assert.Equal("PLC-STABLE-BLOCK", runtime.PlcCode);
        Assert.Empty(runtime.EnabledTaskKeys);
        Assert.Equal($"PlcIoScan_{device.DeviceName}", runtime.ConnectionTask.TaskName);
        Assert.Equal("PLC-STABLE-BLOCK", statusStore.GetSnapshot(device.Id)!.PlcCode);
        Assert.Equal(["PLC-STABLE-BLOCK"], plcServiceFactory.CreatedDeviceNames);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains("基础连接继续", StringComparison.Ordinal)
                     && entry.Message.Contains("PLC-STABLE-BLOCK", StringComparison.Ordinal));

        await runtime.PlcService.DisposeAsync();
        runtime.DisposeCancellation();
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenOneDeviceHasNoBusinessPlan_ShouldStillStartBothConnections()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var blockedDevice = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6100));
        var healthyDevice = networkDevices.Add(CreateLifecyclePlc("PLC-B", 6101));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var plcServiceFactory = new TrackingPlcServiceFactory();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        var coordinator = new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);
        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        try
        {
            Assert.Contains("PLC-A", plcServiceFactory.CreatedDeviceNames);
            Assert.Contains("PLC-B", plcServiceFactory.CreatedDeviceNames);
            var blockedSnapshot = statusStore.GetSnapshot(blockedDevice.Id);
            Assert.NotNull(blockedSnapshot);
            Assert.NotEqual(PlcConnectionState.Faulted, blockedSnapshot!.ConnectionState);
            var healthySnapshot = statusStore.GetSnapshot(healthyDevice.Id);
            Assert.NotNull(healthySnapshot);
            Assert.NotEqual(PlcConnectionState.Faulted, healthySnapshot.ConnectionState);
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == "Info"
                         && entry.Message.Contains("[PlcCode=PLC-B] 初始化完成：仅连接/重试任务已启动", StringComparison.Ordinal));
            Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Message.Contains("Initialized and started", StringComparison.Ordinal)
                         || entry.Message.Contains("task(s)", StringComparison.Ordinal));
        }
        finally
        {
            await coordinator.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TaskPlanApply_DuringRuntimeBuild_ShouldReachTheReplacementRuntime()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6150));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var endpointResolver = new ControlledPlcEndpointResolver();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            new TrackingPlcServiceFactory(),
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            endpointResolver,
            new ModuleHardwareProfileResolver([]));
        var coordinator = new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);
        var controller = new PlcRuntimeTaskController(runtimeRegistry);
        runtimeRegistry.RegisterTaskPlan(
            PlcRuntimeTaskPlan.Empty(
                device.Id,
                device.PlcCode,
                device.DeviceName));
        var replacementPlan = new PlcRuntimeTaskPlan(
            device.Id,
            device.PlcCode,
            device.DeviceName,
            [
                new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                    "Task.MG1",
                    (_, _) => new NoopPlcTask("Task.MG1"))
            ]);

        var initialize = coordinator.InitializeAsync(TestContext.Current.CancellationToken);
        await endpointResolver.ResolveStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        var apply = controller.RegisterAndApplyAsync(
            replacementPlan,
            TestContext.Current.CancellationToken);
        endpointResolver.AllowResolve.TrySetResult();

        await initialize;
        var result = await apply;
        try
        {
            Assert.NotEqual(PlcRuntimeTaskApplyState.WaitingForRuntime, result.State);
            Assert.Equal(["Task.MG1"], result.EnabledTaskKeys);
            Assert.Equal(
                ["Task.MG1"],
                runtimeRegistry.GetTaskPlan(
                    device.Id,
                    device.PlcCode,
                    device.DeviceName).TaskKeys);
            Assert.Equal(
                ["Task.MG1"],
                runtimeRegistry.GetRuntime(device.Id)!.EnabledTaskKeys);
        }
        finally
        {
            await coordinator.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task TaskPlanApply_DuringRuntimeCleanup_ShouldWaitAndRegisterForTheNextRuntime()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6151));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var controlledService = new ControlledDisposePlcService();
        var runtime = CreateInertRuntime(
            device.Id,
            device.DeviceName,
            controlledService,
            logger,
            statusStore);
        Assert.True(runtimeRegistry.TryAddRuntime(runtime));
        var coordinator = CreateLifecycleCoordinator(
            networkDevices,
            ioMappings,
            new TrackingPlcServiceFactory(),
            contextStore,
            logger,
            runtimeRegistry,
            statusStore);
        var controller = new PlcRuntimeTaskController(runtimeRegistry);
        var replacementPlan = new PlcRuntimeTaskPlan(
            device.Id,
            device.PlcCode,
            device.DeviceName,
            [
                new KeyValuePair<string, PlcRuntimeBusinessTaskFactory>(
                    "Task.MG1",
                    (_, _) => new NoopPlcTask("Task.MG1"))
            ]);

        var stop = coordinator.StopDeviceAsync(
            device.Id,
            TestContext.Current.CancellationToken);
        await controlledService.DisposeStarted.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        var apply = controller.RegisterAndApplyAsync(
            replacementPlan,
            TestContext.Current.CancellationToken);

        Assert.False(apply.IsCompleted);

        controlledService.AllowDispose.TrySetResult();
        await stop.WaitAsync(TestContext.Current.CancellationToken);
        var result = await apply.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PlcRuntimeTaskApplyState.WaitingForRuntime, result.State);
        Assert.Null(runtimeRegistry.GetRuntime(device.Id));
        Assert.Equal(
            ["Task.MG1"],
            runtimeRegistry.GetTaskPlan(
                device.Id,
                device.PlcCode,
                device.DeviceName).TaskKeys);

        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenMultiplePlcConnectionsHang_ShouldNotWaitForConnections()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var devices = new[]
        {
            networkDevices.Add(CreateLifecyclePlc("PLC-A", 6200, connectTimeout: 30)),
            networkDevices.Add(CreateLifecyclePlc("PLC-B", 6201, connectTimeout: 30)),
            networkDevices.Add(CreateLifecyclePlc("PLC-C", 6202, connectTimeout: 30))
        };
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var plcServiceFactory = new HangingPlcServiceFactory();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        var coordinator = new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        try
        {
            Assert.Equal(["PLC-A", "PLC-B", "PLC-C"], plcServiceFactory.CreatedDeviceNames);
            Assert.All(devices, device =>
            {
                var snapshot = statusStore.GetSnapshot(device.Id);
                Assert.NotNull(snapshot);
                Assert.False(snapshot!.IsConnected);
                Assert.True(
                    snapshot.ConnectionState is PlcConnectionState.Connecting or PlcConnectionState.Retrying,
                    $"Unexpected connection state for {device.DeviceName}: {snapshot.ConnectionState}");
            });
        }
        finally
        {
            await coordinator.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenEnabledDevicesShareEndpoint_ShouldBlockDuplicateEndpointRuntimes()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var plcA = networkDevices.Add(CreateLifecyclePlc("PLC-A", 6300));
        var plcB = networkDevices.Add(CreateLifecyclePlc("PLC-B", 6300));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var plcServiceFactory = new TrackingPlcServiceFactory();
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        var coordinator = new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);

        await coordinator.InitializeAsync(TestContext.Current.CancellationToken);

        try
        {
            Assert.Contains(logger.Warnings, IsDuplicateEndpointWarning);
            Assert.Empty(plcServiceFactory.CreatedDeviceNames);
            Assert.Empty(runtimeRegistry.GetTrackedDeviceIdsSnapshot());
            AssertDuplicateFault(statusStore.GetSnapshot(plcA.Id));
            AssertDuplicateFault(statusStore.GetSnapshot(plcB.Id));

            logger.Warnings.Clear();
            await coordinator.ReloadDeviceAsync(plcA.Id, TestContext.Current.CancellationToken);

            Assert.Contains(logger.Warnings, IsDuplicateEndpointWarning);
            Assert.Empty(plcServiceFactory.CreatedDeviceNames);
            Assert.Empty(runtimeRegistry.GetTrackedDeviceIdsSnapshot());
            AssertDuplicateFault(statusStore.GetSnapshot(plcA.Id));
            AssertDuplicateFault(statusStore.GetSnapshot(plcB.Id));

            plcB.UpdateEndpoint(plcB.IpAddress, 6301, plcB.Port2, plcB.ConnectTimeout);
            plcServiceFactory.CreatedDeviceNames.Clear();
            logger.Warnings.Clear();
            await coordinator.ReloadDeviceAsync(plcA.Id, TestContext.Current.CancellationToken);

            Assert.DoesNotContain(logger.Warnings, IsDuplicateEndpointWarning);
            Assert.Equal(["PLC-A"], plcServiceFactory.CreatedDeviceNames);
            Assert.Contains(plcA.Id, runtimeRegistry.GetTrackedDeviceIdsSnapshot());
        }
        finally
        {
            await coordinator.StopAsync(TestContext.Current.CancellationToken);
        }

        static bool IsDuplicateEndpointWarning(string message)
            => message.Contains("同一端点 127.0.0.1:6300", StringComparison.Ordinal)
               && message.Contains("PLC-A", StringComparison.Ordinal)
               && message.Contains("PLC-B", StringComparison.Ordinal)
               && message.Contains("已暂停这些 PLC 的运行任务", StringComparison.Ordinal);

        static void AssertDuplicateFault(PlcConnectionRuntimeSnapshot? snapshot)
        {
            Assert.NotNull(snapshot);
            Assert.False(snapshot!.IsConnected);
            Assert.Equal(PlcConnectionState.Faulted, snapshot.ConnectionState);
            Assert.Contains("同一端点 127.0.0.1:6300", snapshot.LastError, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_DisposeAsyncAfterDispose_ShouldJoinSingleCleanupTask()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var service = new ControlledDisposePlcService();
        var runtime = CreateInertRuntime(
            701,
            "PLC-DISPOSE-JOIN",
            service,
            logger,
            statusStore);
        Assert.True(runtimeRegistry.TryAddRuntime(runtime));
        var coordinator = CreateLifecycleCoordinator(
            networkDevices,
            ioMappings,
            new TrackingPlcServiceFactory(),
            contextStore,
            logger,
            runtimeRegistry,
            statusStore);

        coordinator.Dispose();
        await service.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var joinedDispose = coordinator.DisposeAsync().AsTask();

        Assert.False(joinedDispose.IsCompleted);
        Assert.Equal(1, service.DisposeCallCount);

        service.AllowDispose.TrySetResult();
        await joinedDispose.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.DisposeCallCount);
        Assert.Empty(runtimeRegistry.GetTrackedDeviceIdsSnapshot());
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_StopAsyncDuringDispose_ShouldNotRunCleanupTwice()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var service = new ControlledDisposePlcService();
        var runtime = CreateInertRuntime(
            702,
            "PLC-DISPOSE-SERIALIZED",
            service,
            logger,
            statusStore);
        Assert.True(runtimeRegistry.TryAddRuntime(runtime));
        var coordinator = CreateLifecycleCoordinator(
            networkDevices,
            ioMappings,
            new TrackingPlcServiceFactory(),
            contextStore,
            logger,
            runtimeRegistry,
            statusStore);

        coordinator.Dispose();
        await service.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var stopTask = coordinator.StopAsync(TestContext.Current.CancellationToken);

        Assert.False(stopTask.IsCompleted);
        Assert.Equal(1, service.DisposeCallCount);

        service.AllowDispose.TrySetResult();
        await Task.WhenAll(
                coordinator.DisposeAsync().AsTask(),
                stopTask)
            .WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.DisposeCallCount);
        Assert.Empty(runtimeRegistry.GetTrackedDeviceIdsSnapshot());
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenDisposeQuarantines_ShouldKeepReservationAndRejectReloadReplacement()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = networkDevices.Add(CreateLifecyclePlc("PLC-QUARANTINE", 6400));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var replacementFactory = new TrackingPlcServiceFactory();
        var quarantinedService = new QuarantinedDisposePlcService();
        var runtime = CreateInertRuntime(
            device.Id,
            device.DeviceName,
            quarantinedService,
            logger,
            statusStore);
        Assert.True(runtimeRegistry.TryAddRuntime(runtime));
        var coordinator = CreateLifecycleCoordinator(
            networkDevices,
            ioMappings,
            replacementFactory,
            contextStore,
            logger,
            runtimeRegistry,
            statusStore);

        await coordinator.ReloadDeviceAsync(device.Id, TestContext.Current.CancellationToken);

        Assert.Same(runtime, runtimeRegistry.GetRuntime(device.Id));
        Assert.Empty(replacementFactory.CreatedDeviceNames);
        Assert.Equal(1, quarantinedService.DisposeCallCount);
        var snapshot = statusStore.GetSnapshot(device.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(PlcConnectionState.Faulted, snapshot!.ConnectionState);
        Assert.Contains(PlcServiceQuarantinedException.StableReasonCode, snapshot.LastError);
        Assert.Contains(
            logger.Warnings,
            message => message.Contains("禁止创建替代 runtime", StringComparison.Ordinal));

        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task PlcLifecycleCoordinator_WhenReservationChangesDuringCleanup_ShouldRaiseStableRuntimeFault()
    {
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = networkDevices.Add(CreateLifecyclePlc("PLC-RESERVATION", 6401));
        var contextStore = new FakeProductionContextStore();
        var logger = new FakeLogService();
        var runtimeRegistry = new PlcRuntimeRegistry();
        var statusStore = new PlcConnectionStatusStore();
        var factory = new TrackingPlcServiceFactory();
        var controlledService = new ControlledDisposePlcService();
        var originalRuntime = CreateInertRuntime(
            device.Id,
            device.DeviceName,
            controlledService,
            logger,
            statusStore);
        var unexpectedReplacement = CreateInertRuntime(
            device.Id,
            device.DeviceName,
            new ConnectedPlcService(),
            logger,
            statusStore);
        Assert.True(runtimeRegistry.TryAddRuntime(originalRuntime));
        var coordinator = CreateLifecycleCoordinator(
            networkDevices,
            ioMappings,
            factory,
            contextStore,
            logger,
            runtimeRegistry,
            statusStore);

        var reloadTask = coordinator.ReloadDeviceAsync(
            device.Id,
            TestContext.Current.CancellationToken);
        await controlledService.DisposeStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(runtimeRegistry.TryRemoveRuntime(device.Id, originalRuntime));
        Assert.True(runtimeRegistry.TryAddRuntime(unexpectedReplacement));
        controlledService.AllowDispose.TrySetResult();

        await reloadTask.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Same(unexpectedReplacement, runtimeRegistry.GetRuntime(device.Id));
        Assert.Empty(factory.CreatedDeviceNames);
        var snapshot = statusStore.GetSnapshot(device.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(PlcConnectionState.Faulted, snapshot!.ConnectionState);
        Assert.Contains("registry reservation", snapshot.LastError, StringComparison.Ordinal);

        await coordinator.DisposeAsync();
    }

    private static PlcLifecycleCoordinator CreateLifecycleCoordinator(
        InMemoryRepository<NetworkDeviceEntity> networkDevices,
        InMemoryRepository<IoMappingEntity> ioMappings,
        IPlcServiceFactory plcServiceFactory,
        FakeProductionContextStore contextStore,
        FakeLogService logger,
        PlcRuntimeRegistry runtimeRegistry,
        PlcConnectionStatusStore statusStore)
    {
        var runtimeBuilder = new PlcDeviceRuntimeBuilder(
            ioMappings,
            new PlcDataStore(),
            plcServiceFactory,
            contextStore,
            logger,
            statusStore,
            new DefaultPlcSignalBlockPlanner(),
            new StaticPlcEndpointResolver(),
            new ModuleHardwareProfileResolver([]));
        return new PlcLifecycleCoordinator(
            networkDevices,
            contextStore,
            logger,
            runtimeRegistry,
            runtimeBuilder,
            statusStore);
    }

    private static PlcDeviceRuntimeHandle CreateInertRuntime(
        int deviceId,
        string deviceName,
        IPlcService plcService,
        ILogService logger,
        PlcConnectionStatusStore statusStore)
        => new()
        {
            DeviceId = deviceId,
            PlcCode = deviceName,
            DeviceName = deviceName,
            PlcService = plcService,
            Buffer = new PlcBuffer(0, 0),
            Context = new ProductionContext
            {
                PlcCode = deviceName,
                DeviceName = deviceName,
                NetworkDeviceId = deviceId
            },
            ConnectionTask = new NoopPlcTask($"PlcIoScan_{deviceName}"),
            PeriodicReadTask = new NoopPlcTask($"PlcDataReadScan_{deviceName}"),
            ConnectionSignal = new PlcRuntimeConnectionSignal(),
            Logger = logger,
            StatusStore = statusStore,
            CancellationTokenSource = new CancellationTokenSource()
        };

    private sealed class BlockedPlcProductionContextStore : IPlcProductionContextStore
    {
        public PlcProductionContextResolution GetOrCreate(
            PlcIdentity identity,
            string? moduleId = null)
            => PlcProductionContextResolution.Blocked(
                PlcProductionContextResolutionOutcome.MigrationBlocked,
                identity.PlcCode,
                "test_identity_block",
                "test conflict");
    }

    private static readonly IReadOnlyCollection<TaskCandidate> TestCandidates =
    [
        new(
            "Task.A",
            "心跳",
            [
                new TaskRequiredSignal("Signal.Shared", "Read"),
                new TaskRequiredSignal("Signal.Shared", "Write")
            ],
            IsHeartbeatLike: true),
        new(
            "Task.B",
            "业务",
            [new TaskRequiredSignal("Signal.Business", "Read")])
    ];

    private static readonly IReadOnlyCollection<ModuleIoSnapshot> AllTestMappings =
    [
        new("Signal.Shared", "D100", 1, "Int16", "Read", 1, "信号交互", "共享信号"),
        new("Signal.Shared", "D200", 1, "Int16", "Write", 2, "信号交互", "共享信号"),
        new("Signal.Business", "D300", 1, "Int16", "Read", 3, "单点读数据", "业务信号")
    ];

    private static BindingServiceHarness CreateService(
        bool? defaultEnableAllTasks,
        bool seedIoMappings = true)
    {
        _ = defaultEnableAllTasks;
        var runtimeRegistry = new FakeStationRuntimeRegistry(new FakeStationRuntimeFactory());
        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var bindings = new InMemoryRepository<PlcTaskBindingEntity>();
        var logger = new FakeLogService();
        if (seedIoMappings)
        {
            AddTestIoMappings(ioMappings, networkDeviceId: 1);
        }

        var service = new PlcTaskBindingService(
            runtimeRegistry,
            networkDevices,
            ioMappings,
            bindings,
            new TestEdgeUnitOfWorkFactory(bindings));

        return new BindingServiceHarness(service, networkDevices, ioMappings, bindings, logger);
    }

    private static void AddTestIoMappings(
        InMemoryRepository<IoMappingEntity> ioMappings,
        int networkDeviceId,
        bool includeBusinessSignal = true)
    {
        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Shared",
            "D100",
            1,
            "Int16",
            "Read",
            "信号交互",
            "共享信号"));
        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Shared",
            "D200",
            1,
            "Int16",
            "Write",
            "信号交互",
            "共享信号"));
        if (!includeBusinessSignal)
        {
            return;
        }

        ioMappings.Add(IoMappingEntity.Create(
            networkDeviceId,
            "Signal.Business",
            "D300",
            1,
            "Int16",
            "Read",
            "单点读数据",
            "业务信号"));
    }

    private static NetworkDeviceEntity CreateLifecyclePlc(
        string deviceName,
        int port,
        int? connectTimeout = null,
        string? plcCode = null)
    {
        var device = NetworkDeviceEntity.Create(
            deviceName,
            DeviceType.PLC,
            "127.0.0.1",
            port,
            plcCode);
        if (connectTimeout.HasValue)
        {
            device.UpdateEndpoint(device.IpAddress, device.Port1, device.Port2, connectTimeout.Value);
        }

        device.UpdateDeviceModel(PlcType.S7.ToString());
        return device;
    }

    private sealed record BindingServiceHarness(
        PlcTaskBindingService Service,
        InMemoryRepository<NetworkDeviceEntity> NetworkDevices,
        InMemoryRepository<IoMappingEntity> IoMappings,
        InMemoryRepository<PlcTaskBindingEntity> Bindings,
        FakeLogService Logger);

    private static async Task SaveBindingsForTestAsync(
        PlcTaskBindingService service,
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken)
    {
        var preparation = await service
            .PrepareAsync(networkDeviceId, moduleId, taskStates, cancellationToken)
            .ConfigureAwait(false);
        await service.CommitAsync(preparation, cancellationToken).ConfigureAwait(false);
    }

    private sealed class FakeStationRuntimeRegistry(IStationRuntimeFactory factory) : IStationRuntimeRegistry
    {
        public void Register(IStationRuntimeFactory runtimeFactory)
        {
        }

        public bool HasFactory(string moduleId)
            => string.Equals(moduleId, factory.ModuleId, StringComparison.OrdinalIgnoreCase);

        public bool TryGetFactory(string moduleId, out IStationRuntimeFactory runtimeFactory)
        {
            if (HasFactory(moduleId))
            {
                runtimeFactory = factory;
                return true;
            }

            runtimeFactory = null!;
            return false;
        }

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations()
            => new Dictionary<string, IStationRuntimeFactory>(StringComparer.OrdinalIgnoreCase)
            {
                [factory.ModuleId] = factory
            };
    }

    private sealed class FakeStationRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => "TestModule";

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
            => TestCandidates;

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }

    private sealed class FakeLogService : ILogService
    {
        public List<LogEntry> Entries { get; } = [];

        public List<string> Warnings { get; } = [];

        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Add("Debug", message);
        public void Info(string message) => Add("Info", message);
        public void Warn(string message)
        {
            Warnings.Add(message);
            Add("Warn", message);
        }

        public void Error(string message) => Add("Error", message);
        public void Fatal(string message) => Add("Fatal", message);

        private void Add(string level, string message)
        {
            var entry = new LogEntry
            {
                Level = level,
                Message = message,
                Time = DateTime.UtcNow
            };
            Entries.Add(entry);
            EntryAdded?.Invoke(entry);
        }
    }

    private sealed class FakeProductionTimeProvider : IProductionTimeProvider
    {
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Local;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime BusinessNow => DateTime.Now;
        public DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        public DateTime ToBusinessTime(DateTime value) => value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
        public string FormatBusinessTimestamp(DateTime value) => ToBusinessTime(value).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private sealed class NoopPlcTask(string taskName) : IPlcTask, IStartupAwareBackgroundTask
    {
        public string TaskName { get; } = taskName;

        public Task StartAsync(CancellationToken ct)
            => RunAsync(ct);

        public BackgroundTaskRun StartWithStartup(CancellationToken cancellationToken)
            => new(Task.CompletedTask, RunAsync(cancellationToken));

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

    private sealed class TrackingPlcServiceFactory : IPlcServiceFactory
    {
        public List<string> CreatedDeviceNames { get; } = [];

        public IPlcService Create(PlcType plcType, string deviceName)
        {
            CreatedDeviceNames.Add(deviceName);
            return new ConnectedPlcService();
        }
    }

    private sealed class StaticPlcEndpointResolver : IPlcEndpointResolver
    {
        public Task<PlcEndpoint> ResolveAsync(
            NetworkDeviceEntity device,
            PlcType plcType,
            CancellationToken cancellationToken = default)
            => Task.FromResult<PlcEndpoint>(
                new TcpPlcEndpoint(device.IpAddress, device.Port1, device.ConnectTimeout));
    }

    private sealed class ControlledPlcEndpointResolver : IPlcEndpointResolver
    {
        public TaskCompletionSource ResolveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowResolve { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<PlcEndpoint> ResolveAsync(
            NetworkDeviceEntity device,
            PlcType plcType,
            CancellationToken cancellationToken = default)
        {
            ResolveStarted.TrySetResult();
            await AllowResolve.Task.WaitAsync(cancellationToken);
            return new TcpPlcEndpoint(
                device.IpAddress,
                device.Port1,
                device.ConnectTimeout);
        }
    }

    private sealed class ConnectedPlcService : PlcServiceTestDouble
    {
        public override bool IsConnected { get; protected set; }

        public override Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public override Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public override Task<List<T>> ReadDataAsync<T>(
            string address,
            ushort length,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Repeat(default(T)!, length).ToList());

        public override Task WriteDataAsync<T>(
            string address,
            List<T> data,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControlledDisposePlcService : PlcServiceTestDouble
    {
        private int _disposeCallCount;

        public TaskCompletionSource DisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowDispose { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCallCount => Volatile.Read(ref _disposeCallCount);

        public override async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCallCount);
            DisposeStarted.TrySetResult();
            await AllowDispose.Task.ConfigureAwait(false);
        }
    }

    private sealed class QuarantinedDisposePlcService : PlcServiceTestDouble
    {
        public int DisposeCallCount { get; private set; }

        public override ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.FromException(new PlcServiceQuarantinedException(
                nameof(QuarantinedDisposePlcService),
                nameof(DisposeAsync),
                "protocol task did not settle"));
        }
    }

    private sealed class HangingPlcServiceFactory : IPlcServiceFactory
    {
        public List<string> CreatedDeviceNames { get; } = [];

        public IPlcService Create(PlcType plcType, string deviceName)
        {
            CreatedDeviceNames.Add(deviceName);
            return new HangingPlcService();
        }
    }

    private sealed class HangingPlcService : PlcServiceTestDouble
    {
        public override Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
            => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private int _nextId = 1;

        public List<T> Items { get; } = [];

        public IQueryable<T> GetQueryable() => Items.AsQueryable();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                EntityIdTestHelper.SetId(entity, _nextId++);
            }

            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
            => Items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult(Items.RemoveAll(item => compiled(item)));
        }

        public async Task<int> ReplaceAsync(
            Expression<Func<T, bool>> predicate,
            IReadOnlyCollection<T> replacements,
            CancellationToken cancellationToken = default)
        {
            var affected = await ExecuteDeleteAsync(predicate, cancellationToken);
            foreach (var replacement in replacements)
            {
                Add(replacement);
                affected++;
            }

            return affected;
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(Items.FirstOrDefault(item => EqualityComparer<object>.Default.Equals(item.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(expression.Compile()));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.Count(expression.Compile()));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
