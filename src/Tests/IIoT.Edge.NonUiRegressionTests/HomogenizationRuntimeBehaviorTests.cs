using IIoT.Edge.Application.Modules.Hardware;
using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Homogenization.Samples;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot>;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationRuntimeBehaviorTests
{
    private static readonly HomogenizationCodeOptions TestCodeOptions = CreateCodeOptions();

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenEnabled_ShouldImportSeedDeviceAndMappingsIdempotently()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Homogenization:IoSeed:Enabled"] = "true",
                ["Modules:Homogenization:IoSeed:ResetBeforeImport"] = "false"
            })
            .Build();

        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var contributor = new HomogenizationDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            new FakeLogService(),
            [new HomogenizationHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();
        await contributor.EnsureConfigurationSamplesAsync();

        var device = Assert.Single(networkDevices.Items);
        Assert.Equal(DependencyInjection.ModuleKey, device.ModuleId);
        Assert.Equal(DeviceType.PLC, device.DeviceType);
        Assert.Equal(HomogenizationPlcSignalProfile.Signals.Count, ioMappings.Items.Count);
        Assert.Equal(
            HomogenizationPlcSignalProfile.Signals.Select(static signal => signal.Label).OrderBy(static label => label),
            ioMappings.Items.Select(static mapping => mapping.Label).OrderBy(static label => label));
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "信号交互" && mapping.GroupName == "扫码进站");
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "连续读数据" && mapping.DisplayRole == "托盘码");
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "单点读数据" && mapping.GroupName == "实时数据");
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenExistingMappingsHaveOldClassification_ShouldRepairMetadataAndKeepAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Homogenization:IoSeed:Enabled"] = "true",
                ["Modules:Homogenization:IoSeed:ResetBeforeImport"] = "false"
            })
            .Build();

        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = NetworkDeviceEntity.Create("PLC-Homogenization-01", DeviceType.PLC, "127.0.0.1", 6000);
        device.AssignModule(DependencyInjection.ModuleKey, "Mc");
        device.UpdateEndpoint("127.0.0.1", 6000, null, 3000);
        device.Enable();
        networkDevices.Add(device);

        var legacyMapping = IoMappingEntity.Create(
            device.Id,
            "Homogenization.InboundTrigger",
            "D999",
            1,
            "UInt16",
            "Read",
            "单点读数据",
            "单点读数据",
            "");
        legacyMapping.UpdateSortOrder(999);
        legacyMapping.UpdateMetadata(
            legacyMapping.Label,
            legacyMapping.DataType,
            legacyMapping.Direction,
            legacyMapping.Category,
            legacyMapping.GroupName,
            legacyMapping.DisplayRole,
            "旧分类");
        ioMappings.Add(legacyMapping);

        var contributor = new HomogenizationDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            new FakeLogService(),
            [new HomogenizationHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();

        var repaired = ioMappings.Items.Single(static mapping => mapping.Label == "Homogenization.InboundTrigger");
        Assert.Equal("D999", repaired.PlcAddress);
        Assert.Equal(1, repaired.AddressCount);
        Assert.Equal("UInt16", repaired.DataType);
        Assert.Equal("Read", repaired.Direction);
        Assert.Equal("信号交互", repaired.Category);
        Assert.Equal("扫码进站", repaired.GroupName);
        Assert.Equal("PLC 触发", repaired.DisplayRole);
        Assert.Equal(2, repaired.SortOrder);
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenResetEnabled_ShouldOnlyReplaceHomogenizationData()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Homogenization:IoSeed:Enabled"] = "true",
                ["Modules:Homogenization:IoSeed:ResetBeforeImport"] = "true"
            })
            .Build();

        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();

        var otherDevice = NetworkDeviceEntity.Create("PLC-Other", DeviceType.PLC, "10.0.0.2", 102);
        otherDevice.AssignModule("OtherModule", "S7");
        otherDevice.UpdateEndpoint("10.0.0.2", 102, null, 3000);
        otherDevice.Enable();
        networkDevices.Add(otherDevice);

        var oldHomogenizationDevice = NetworkDeviceEntity.Create("PLC-Old-H", DeviceType.PLC, "10.0.0.3", 6000);
        oldHomogenizationDevice.AssignModule(DependencyInjection.ModuleKey, "Mc");
        oldHomogenizationDevice.UpdateEndpoint("10.0.0.3", 6000, null, 3000);
        oldHomogenizationDevice.Enable();
        networkDevices.Add(oldHomogenizationDevice);

        var otherMapping = IoMappingEntity.Create(otherDevice.Id, "Other.Signal", "DB1.DBW0", 1, "Int16", "Read");
        otherMapping.UpdateSortOrder(1);
        ioMappings.Add(otherMapping);

        var legacyHomogenizationMapping = IoMappingEntity.Create(oldHomogenizationDevice.Id, "Homogenization.Legacy", "D0", 1, "Int16", "Read");
        legacyHomogenizationMapping.UpdateSortOrder(1);
        ioMappings.Add(legacyHomogenizationMapping);

        var contributor = new HomogenizationDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            new FakeLogService(),
            [new HomogenizationHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();

        Assert.Contains(networkDevices.Items, static device => device.ModuleId == "OtherModule");
        Assert.Contains(ioMappings.Items, static mapping => mapping.Label == "Other.Signal");

        var homogenizationDevices = networkDevices.Items
            .Where(static device => device.ModuleId == DependencyInjection.ModuleKey)
            .ToArray();

        Assert.Single(homogenizationDevices);
        Assert.Equal("PLC-Homogenization-01", homogenizationDevices[0].DeviceName);
        Assert.DoesNotContain(ioMappings.Items, static mapping => mapping.Label == "Homogenization.Legacy");
        Assert.Equal(
            HomogenizationPlcSignalProfile.Signals.Count + 1,
            ioMappings.Items.Count);
    }

    [Fact]
    public async Task HomogenizationStationRuntimeFactory_WhenBindingsReordered_ShouldHandleInboundAndOutboundByLabel()
    {
        var logger = new FakeLogService();
        var pipeline = new BlockingDataPipelineService();
        var diagnostics = new FakeMesUploadDiagnosticsStore();
        var mesApi = new CapturingHomogenizationMesChannel();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-H",
            ClientCode = "CLIENT-H",
            ProcessId = Guid.NewGuid()
        });

        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDeviceService>(deviceService);
        services.AddSingleton<IMesUploadDiagnosticsStore>(diagnostics);
        services.AddSingleton<HomogenizationMesScenarioChannel>(mesApi);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IProductionTimeProvider>(new FakeProductionTimeProvider());
        services.AddSingleton<IModuleParamProvider<MesParam, CloudParam, BusinessParam>>(new RuntimeFakeModuleParamProvider());
        services.AddSingleton(new HomogenizationTrayCodeGuard());
        services.AddSingleton(new HomogenizationCellDataValidator());
        services.AddSingleton(Options.Create(new HomogenizationModuleOptions()));
        services.AddSingleton(Options.Create(TestCodeOptions));
        using var provider = services.BuildServiceProvider();

        var bindings = BuildInboundOutboundBindings();
        var readOffsets = BuildOffsets(bindings, "Read");
        var writeOffsets = BuildOffsets(bindings, "Write");
        var buffer = new PlcBuffer(GetBufferSize(bindings, "Read"), GetBufferSize(bindings, "Write"));
        var context = new HomogenizationContext
        {
            DeviceName = "PLC-H",
            DeviceId = 7
        };

        ProductionContextSignalBindings.Set(context, bindings);

        var readValues = new ushort[GetBufferSize(bindings, "Read")];
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.HeartbeatIn.Label, 7);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.InboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusValue.Label, 0);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeStirringSpeed.Label, 123);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeStirringCurrent.Label, 11);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeDispersionSpeed.Label, 222);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeDispersionCurrent.Label, 12);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeTemperature.Label, 26);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeVacuum.Label, unchecked((ushort)(short)-9));
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundCntActual.Label, 15);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundCntTarget.Label, 18);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundCntTankAWeight.Label, 2);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundCntTankBWeight.Label, 3);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundNmpActual.Label, 27);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundNmpTarget.Label, 30);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundGlueActual.Label, 31);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundSetStirringTime.Label, 40);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundRemainingStirringTime.Label, 5);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundSetDispersionTime.Label, 50);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundRemainingDispersionTime.Label, 6);
        SetAscii(readValues, readOffsets, HomogenizationPlcSignalProfile.TrayCode.Label, "TRAY-9001", 30);
        buffer.UpdateReadBuffer(readValues);

        var tasks = new HomogenizationStationRuntimeFactory().CreateTasks(provider, buffer, context);
        using var cancellation = new CancellationTokenSource();
        var runningTasks = tasks.Select(task => task.StartAsync(cancellation.Token)).ToArray();

        await WaitUntilAsync(() =>
            buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.HeartbeatOut.Label]] == 7
            && mesApi.LastRealtimeSnapshot is not null
            && context.LastRealtimeSnapshot is not null);
        Assert.Equal((ushort)7, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.HeartbeatOut.Label]]);
        Assert.NotNull(mesApi.LastRealtimeSnapshot);
        Assert.Equal(123, mesApi.LastRealtimeSnapshot!.StirringSpeed);
        Assert.Equal(26, context.LastRealtimeSnapshot!.Temperature);
        Assert.Equal(0, context.GetStep("Homogenization.Inbound"));
        Assert.Equal(0, context.GetStep("Homogenization.EquipmentStatus"));
        Assert.Equal(0, context.GetStep("Homogenization.Outbound"));

        mesApi.InboundGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.InboundTrigger.Label, TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 10);
        await WaitUntilAsync(() => mesApi.InboundTrayCodes.Contains("TRAY-9001"));
        Assert.Equal(10, context.GetStep("Homogenization.Inbound"));
        mesApi.InboundGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 30);

        Assert.Contains("TRAY-9001", mesApi.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.InboundAck.Label]]);

        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.InboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.InboundAck.Label]]);

        mesApi.EquipmentStatusGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusValue.Label, 1);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 10);
        await WaitUntilAsync(() => mesApi.LastEquipmentStatusSnapshot is not null);
        Assert.Equal(10, context.GetStep("Homogenization.EquipmentStatus"));
        mesApi.EquipmentStatusGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.NotNull(mesApi.LastEquipmentStatusSnapshot);
        Assert.Equal(1, mesApi.LastEquipmentStatusSnapshot!.StatusCode);
        Assert.Equal("空闲", mesApi.LastEquipmentStatusSnapshot.StatusText);
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.EquipmentStatusAck.Label]]);

        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.EquipmentStatusAck.Label]]);

        pipeline.EnqueueGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundTrigger.Label, TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Outbound") == 10);
        await WaitUntilAsync(() => pipeline.PendingCount == 1);
        Assert.Equal(10, context.GetStep("Homogenization.Outbound"));
        pipeline.EnqueueGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Outbound") == 30);

        Assert.True(pipeline.TryDequeue(out var record));
        var cellData = Assert.IsType<HomogenizationCellData>(record!.CellData);
        Assert.Equal("TRAY-9001", cellData.TrayCode);
        Assert.Equal(123, cellData.RealtimeSnapshot!.StirringSpeed);
        Assert.Equal(26, cellData.RealtimeSnapshot.Temperature);
        Assert.Equal(-9, cellData.RealtimeSnapshot.Vacuum);
        Assert.Equal(15d, cellData.CntActualKg);
        Assert.Equal(31d, cellData.GlueActualKg);
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.OutboundAck.Label]]);

        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Outbound") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.OutboundAck.Label]]);

        cancellation.Cancel();
        await Task.WhenAll(runningTasks);
    }

    [Fact]
    public async Task HomogenizationStationRuntimeFactory_WhenRecipeBindingsReordered_ShouldDecodeFloatAndArrayValuesByLabel()
    {
        var logger = new FakeLogService();
        var diagnostics = new FakeMesUploadDiagnosticsStore();
        var pipeline = new FakeDataPipelineService();
        var mesApi = new CapturingHomogenizationMesChannel();
        var deviceService = new FakeDeviceService();
        deviceService.SetOnline(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-H",
            ClientCode = "CLIENT-H",
            ProcessId = Guid.NewGuid()
        });

        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDeviceService>(deviceService);
        services.AddSingleton<IMesUploadDiagnosticsStore>(diagnostics);
        services.AddSingleton<HomogenizationMesScenarioChannel>(mesApi);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IProductionTimeProvider>(new FakeProductionTimeProvider());
        services.AddSingleton<IModuleParamProvider<MesParam, CloudParam, BusinessParam>>(new RuntimeFakeModuleParamProvider());
        services.AddSingleton(new HomogenizationTrayCodeGuard());
        services.AddSingleton(new HomogenizationCellDataValidator());
        services.AddSingleton(Options.Create(new HomogenizationModuleOptions()));
        services.AddSingleton(Options.Create(TestCodeOptions));
        using var provider = services.BuildServiceProvider();

        var bindings = BuildRecipeBindings();
        var readOffsets = BuildOffsets(bindings, "Read");
        var writeOffsets = BuildOffsets(bindings, "Write");
        var buffer = new PlcBuffer(GetBufferSize(bindings, "Read"), GetBufferSize(bindings, "Write"));
        var context = new HomogenizationContext
        {
            DeviceName = "PLC-H",
            DeviceId = 8
        };
        ProductionContextSignalBindings.Set(context, bindings);

        var readValues = new ushort[GetBufferSize(bindings, "Read")];
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.HeartbeatIn.Label, 3);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.InboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.OutboundTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeStirringSpeed.Label, 101);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeStirringCurrent.Label, 11);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeDispersionSpeed.Label, 202);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeDispersionCurrent.Label, 12);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeTemperature.Label, 30);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RealtimeVacuum.Label, unchecked((ushort)(short)-12));
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.EquipmentStatusValue.Label, 0);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeStirringSpeed.Label, 55);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeDispersionSpeed.Label, 66);
        SetFloat(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeNcm.Label, 1, 12.5f);
        SetFloat(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeSp1.Label, 2, 8.75f);
        SetFloat(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeNmp.Label, 0, 99.25f);
        SetFloat(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeGlueSolution.Label, 0, 5.5f);
        SetFloat(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeCnt.Label, 0, 2.25f);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeVacuum.Label, 1);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTime.Label, 0, 15);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTemperature.Label, 0, 45);
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeStopStep.Label, 0, 1);
        buffer.UpdateReadBuffer(readValues);

        var tasks = new HomogenizationStationRuntimeFactory().CreateTasks(provider, buffer, context);
        using var cancellation = new CancellationTokenSource();
        var runningTasks = tasks.Select(task => task.StartAsync(cancellation.Token)).ToArray();

        await WaitUntilAsync(() =>
            buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.HeartbeatOut.Label]] == 3);
        Assert.Equal(0, context.GetStep("Homogenization.Recipe"));

        mesApi.RecipeGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTrigger.Label, TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Recipe") == 10);
        await WaitUntilAsync(() => mesApi.LastRecipeSnapshot is not null);
        Assert.Equal(10, context.GetStep("Homogenization.Recipe"));
        mesApi.RecipeGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Recipe") == 30);

        Assert.NotNull(mesApi.LastRecipeSnapshot);
        Assert.Equal(55, mesApi.LastRecipeSnapshot!.StirringSpeed[0]);
        Assert.Equal(66, mesApi.LastRecipeSnapshot.DispersionSpeed[0]);
        Assert.Equal(12.5d, mesApi.LastRecipeSnapshot.Ncm[1], 3);
        Assert.Equal(8.75d, mesApi.LastRecipeSnapshot.Sp1[2], 3);
        Assert.Equal(99.25d, mesApi.LastRecipeSnapshot.Nmp[0], 3);
        Assert.True(mesApi.LastRecipeSnapshot.Vacuum[0]);
        Assert.Equal(15, mesApi.LastRecipeSnapshot.Time[0]);
        Assert.Equal(45d, mesApi.LastRecipeSnapshot.Temperature[0], 3);
        Assert.True(mesApi.LastRecipeSnapshot.StopStep[0]);

        SetWord(readValues, readOffsets, HomogenizationPlcSignalProfile.RecipeTrigger.Label, TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Recipe") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationPlcSignalProfile.RecipeAck.Label]]);

        cancellation.Cancel();
        await Task.WhenAll(runningTasks);
    }

    private static IReadOnlyList<ModuleIoSnapshot> BuildInboundOutboundBindings()
        =>
        [
            new(HomogenizationPlcSignalProfile.OutboundCntActual.Label, "D3030", 1, "UInt16", "Read", 1),
            new(HomogenizationPlcSignalProfile.InboundTrigger.Label, "D701", 1, "Int16", "Read", 2),
            new(HomogenizationPlcSignalProfile.TrayCode.Label, "D24500", 30, "Ascii", "Read", 3),
            new(HomogenizationPlcSignalProfile.RealtimeTemperature.Label, "D301", 1, "Int16", "Read", 4),
            new(HomogenizationPlcSignalProfile.RealtimeVacuum.Label, "D300", 1, "Int16", "Read", 5),
            new(HomogenizationPlcSignalProfile.HeartbeatIn.Label, "D700", 1, "Int16", "Read", 6),
            new(HomogenizationPlcSignalProfile.OutboundTrigger.Label, "D702", 1, "Int16", "Read", 7),
            new(HomogenizationPlcSignalProfile.RecipeTrigger.Label, "D703", 1, "Int16", "Read", 8),
            new(HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, "D707", 1, "Int16", "Read", 9),
            new(HomogenizationPlcSignalProfile.RealtimeStirringSpeed.Label, "D1618", 1, "Int16", "Read", 10),
            new(HomogenizationPlcSignalProfile.RealtimeStirringCurrent.Label, "D1616", 1, "Int16", "Read", 11),
            new(HomogenizationPlcSignalProfile.RealtimeDispersionSpeed.Label, "D1638", 1, "Int16", "Read", 12),
            new(HomogenizationPlcSignalProfile.RealtimeDispersionCurrent.Label, "D1636", 1, "Int16", "Read", 13),
            new(HomogenizationPlcSignalProfile.EquipmentStatusValue.Label, "D711", 1, "Int16", "Read", 14),
            new(HomogenizationPlcSignalProfile.OutboundCntTarget.Label, "D8000", 1, "UInt16", "Read", 15),
            new(HomogenizationPlcSignalProfile.OutboundCntTankAWeight.Label, "D7000", 1, "UInt16", "Read", 16),
            new(HomogenizationPlcSignalProfile.OutboundCntTankBWeight.Label, "D7002", 1, "UInt16", "Read", 17),
            new(HomogenizationPlcSignalProfile.OutboundNmpActual.Label, "D812", 1, "UInt16", "Read", 18),
            new(HomogenizationPlcSignalProfile.OutboundNmpTarget.Label, "D810", 1, "UInt16", "Read", 19),
            new(HomogenizationPlcSignalProfile.OutboundGlueActual.Label, "D822", 1, "UInt16", "Read", 20),
            new(HomogenizationPlcSignalProfile.OutboundSetStirringTime.Label, "D2054", 1, "UInt16", "Read", 21),
            new(HomogenizationPlcSignalProfile.OutboundRemainingStirringTime.Label, "D2056", 1, "UInt16", "Read", 22),
            new(HomogenizationPlcSignalProfile.OutboundSetDispersionTime.Label, "D2044", 1, "UInt16", "Read", 23),
            new(HomogenizationPlcSignalProfile.OutboundRemainingDispersionTime.Label, "D2046", 1, "UInt16", "Read", 24),
            new(HomogenizationPlcSignalProfile.OutboundAck.Label, "D602", 1, "Int16", "Write", 1),
            new(HomogenizationPlcSignalProfile.HeartbeatOut.Label, "D600", 1, "Int16", "Write", 2),
            new(HomogenizationPlcSignalProfile.InboundAck.Label, "D601", 1, "Int16", "Write", 3),
            new(HomogenizationPlcSignalProfile.RecipeAck.Label, "D603", 1, "Int16", "Write", 4),
            new(HomogenizationPlcSignalProfile.EquipmentStatusAck.Label, "D607", 1, "Int16", "Write", 5)
        ];

    private static HomogenizationCodeOptions CreateCodeOptions()
        => new()
        {
            Plc = new HomogenizationPlcCodeOptions
            {
                SignalReset = 10,
                SignalTrigger = 11,
                AckOk = 11,
                AckException = 12,
                AckMesNg = 13
            },
            Mes = new HomogenizationMesCodeOptions
            {
                Channels = new HomogenizationMesChannelOptions
                {
                    Inbound = "Homogenization.Inbound",
                    Outbound = "Homogenization",
                    Realtime = "Homogenization.Realtime",
                    Recipe = "Homogenization.Recipe",
                    EquipmentStatus = "Homogenization.EquipmentStatus"
                },
                EquipmentStatusTexts = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["-1"] = "报警",
                    ["0"] = "运行中",
                    ["1"] = "空闲",
                    ["2"] = "离线",
                    ["3"] = "待料"
                }
            }
        };

    private static IReadOnlyList<ModuleIoSnapshot> BuildRecipeBindings()
        =>
        [
            new(HomogenizationPlcSignalProfile.RecipeNmp.Label, "ZR1200", 60, "Float", "Read", 1),
            new(HomogenizationPlcSignalProfile.RecipeTrigger.Label, "D703", 1, "Int16", "Read", 2),
            new(HomogenizationPlcSignalProfile.HeartbeatIn.Label, "D700", 1, "Int16", "Read", 3),
            new(HomogenizationPlcSignalProfile.RecipeStirringSpeed.Label, "ZR400", 30, "UInt16", "Read", 4),
            new(HomogenizationPlcSignalProfile.RealtimeStirringSpeed.Label, "D1618", 1, "Int16", "Read", 5),
            new(HomogenizationPlcSignalProfile.InboundTrigger.Label, "D701", 1, "Int16", "Read", 6),
            new(HomogenizationPlcSignalProfile.OutboundTrigger.Label, "D702", 1, "Int16", "Read", 7),
            new(HomogenizationPlcSignalProfile.EquipmentStatusTrigger.Label, "D707", 1, "Int16", "Read", 8),
            new(HomogenizationPlcSignalProfile.EquipmentStatusValue.Label, "D711", 1, "Int16", "Read", 9),
            new(HomogenizationPlcSignalProfile.RealtimeStirringCurrent.Label, "D1616", 1, "Int16", "Read", 10),
            new(HomogenizationPlcSignalProfile.RealtimeDispersionSpeed.Label, "D1638", 1, "Int16", "Read", 11),
            new(HomogenizationPlcSignalProfile.RealtimeDispersionCurrent.Label, "D1636", 1, "Int16", "Read", 12),
            new(HomogenizationPlcSignalProfile.RealtimeTemperature.Label, "D301", 1, "Int16", "Read", 13),
            new(HomogenizationPlcSignalProfile.RealtimeVacuum.Label, "D300", 1, "Int16", "Read", 14),
            new(HomogenizationPlcSignalProfile.RecipeDispersionSpeed.Label, "ZR500", 30, "UInt16", "Read", 15),
            new(HomogenizationPlcSignalProfile.RecipeNcm.Label, "ZR1000", 60, "Float", "Read", 16),
            new(HomogenizationPlcSignalProfile.RecipeSp1.Label, "ZR1800", 60, "Float", "Read", 17),
            new(HomogenizationPlcSignalProfile.RecipeGlueSolution.Label, "ZR1400", 60, "Float", "Read", 18),
            new(HomogenizationPlcSignalProfile.RecipeCnt.Label, "ZR1600", 60, "Float", "Read", 19),
            new(HomogenizationPlcSignalProfile.RecipeVacuum.Label, "R300", 30, "Bool", "Read", 20),
            new(HomogenizationPlcSignalProfile.RecipeTime.Label, "ZR0", 30, "UInt16", "Read", 21),
            new(HomogenizationPlcSignalProfile.RecipeTemperature.Label, "ZR100", 30, "Int16", "Read", 22),
            new(HomogenizationPlcSignalProfile.RecipeStopStep.Label, "ZR200", 30, "Bool", "Read", 23),
            new(HomogenizationPlcSignalProfile.HeartbeatOut.Label, "D600", 1, "Int16", "Write", 1),
            new(HomogenizationPlcSignalProfile.RecipeAck.Label, "D603", 1, "Int16", "Write", 2)
        ];

    private static Dictionary<string, int> BuildOffsets(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
    {
        var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentOffset = 0;

        foreach (var binding in bindings
                     .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(binding => binding.SortOrder))
        {
            offsets[binding.Label] = currentOffset;
            currentOffset += Math.Max(1, binding.AddressCount);
        }

        return offsets;
    }

    private static int GetBufferSize(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
        => bindings
            .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => binding.SortOrder)
            .Sum(static binding => Math.Max(1, binding.AddressCount));

    private static void SetWord(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, ushort value)
        => buffer[offsets[label]] = value;

    private static void SetWord(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, int valueIndex, ushort value)
        => buffer[offsets[label] + valueIndex] = value;

    private static void SetAscii(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, string value, int wordCount)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var lowIndex = wordIndex * 2;
            var highIndex = lowIndex + 1;
            var low = lowIndex < bytes.Length ? bytes[lowIndex] : (byte)0;
            var high = highIndex < bytes.Length ? bytes[highIndex] : (byte)0;
            buffer[offsets[label] + wordIndex] = (ushort)(low | (high << 8));
        }
    }

    private static void SetFloat(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string label, int valueIndex, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var baseOffset = offsets[label] + (valueIndex * 2);
        buffer[baseOffset] = (ushort)((bytes[2] << 8) | bytes[3]);
        buffer[baseOffset + 1] = (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.True(condition());
    }

    private sealed class RuntimeFakeModuleParamProvider
        : IModuleParamProvider<MesParam, CloudParam, BusinessParam>
    {
        public Task<ModuleParamSnapshot<MesParam, CloudParam, BusinessParam>> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleParamSnapshot<MesParam, CloudParam, BusinessParam>(
                DependencyInjection.ModuleKey,
                EmptyGroup<MesParam>(ModuleParamCategory.Mes),
                EmptyGroup<CloudParam>(ModuleParamCategory.Cloud),
                new ModuleParamGroup<BusinessParam>(
                    DependencyInjection.ModuleKey,
                    ModuleParamCategory.Business,
                    new Dictionary<BusinessParam, string>(),
                    new Dictionary<BusinessParam, string?>
                    {
                        [BusinessParam.启用托盘码重码验证] = "false"
                    },
                    new Dictionary<BusinessParam, ParamValueKind>
                    {
                        [BusinessParam.启用托盘码重码验证] = ParamValueKind.Bool
                    },
                    warn: null)));

        private static ModuleParamGroup<TEnum> EmptyGroup<TEnum>(ModuleParamCategory category)
            where TEnum : struct, Enum
            => new(
                DependencyInjection.ModuleKey,
                category,
                new Dictionary<TEnum, string>(),
                new Dictionary<TEnum, string?>(),
                new Dictionary<TEnum, ParamValueKind>(),
                warn: null);
    }

    private sealed class CapturingHomogenizationMesChannel : HomogenizationMesScenarioChannel
    {
        public List<string> InboundTrayCodes { get; } = [];

        public string ProcessType => DependencyInjection.ModuleKey;

        public MesUploadMode UploadMode => MesUploadMode.Single;

        public TaskCompletionSource<bool>? InboundGate { get; set; }

        public TaskCompletionSource<bool>? RecipeGate { get; set; }

        public TaskCompletionSource<bool>? EquipmentStatusGate { get; set; }

        public HomogenizationRecipeSnapshot? LastRecipeSnapshot { get; private set; }

        public HomogenizationRealtimeSnapshot? LastRealtimeSnapshot { get; private set; }

        public HomogenizationEquipmentStatusSnapshot? LastEquipmentStatusSnapshot { get; private set; }

        public Task<MesCallResult> UploadInboundAsync(
            DeviceSession? device,
            string trayCode,
            CancellationToken cancellationToken = default)
        {
            InboundTrayCodes.Add(trayCode);
            return CompleteAfterGateAsync(InboundGate, cancellationToken);
        }

        public Task<MesCallResult> UploadOutboundAsync(
            DeviceSession? device,
            HomogenizationCellData cellData,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            HomogenizationRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            LastRealtimeSnapshot = snapshot;
            return Task.FromResult(MesCallResult.Success());
        }

        public Task<MesCallResult> UploadRecipeAsync(
            DeviceSession? device,
            HomogenizationRecipeSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            LastRecipeSnapshot = snapshot;
            return CompleteAfterGateAsync(RecipeGate, cancellationToken);
        }

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            HomogenizationEquipmentStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            LastEquipmentStatusSnapshot = snapshot;
            return CompleteAfterGateAsync(EquipmentStatusGate, cancellationToken);
        }

        public Task<MesCallResult> UploadAsync(
            ProcessMesUploadContext context,
            IReadOnlyList<CellCompletedRecord> records,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        private static async Task<MesCallResult> CompleteAfterGateAsync(
            TaskCompletionSource<bool>? gate,
            CancellationToken cancellationToken)
        {
            if (gate is not null)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            return MesCallResult.Success();
        }
    }

    private sealed class BlockingDataPipelineService : IDataPipelineService
    {
        private readonly Queue<CellCompletedRecord> _queue = new();

        public TaskCompletionSource<bool>? EnqueueGate { get; set; }

        public int PendingCount => _queue.Count;

        public int OverflowCount => 0;

        public int SpillCount => 0;

        public ValueTask<DataPipelineEnqueueResult> EnqueueAsync(
            CellCompletedRecord record,
            CancellationToken cancellationToken = default)
            => EnqueueAsyncCore(record, cancellationToken);

        public bool TryDequeue(out CellCompletedRecord? record)
        {
            if (_queue.Count == 0)
            {
                record = null;
                return false;
            }

            record = _queue.Dequeue();
            return true;
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_queue.Count > 0);

        private async ValueTask<DataPipelineEnqueueResult> EnqueueAsyncCore(
            CellCompletedRecord record,
            CancellationToken cancellationToken)
        {
            _queue.Enqueue(record);
            if (EnqueueGate is not null)
            {
                await EnqueueGate.Task.WaitAsync(cancellationToken);
            }

            return DataPipelineEnqueueResult.Accepted();
        }
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
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);

        public Task<int> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            var deleted = Items.RemoveAll(item => compiled(item));
            return Task.FromResult(deleted);
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
