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
using IIoT.Edge.Module.Homogenization.Integration;
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
                ["Modules:Homogenization:DeviceSeed:Enabled"] = "true",
                ["Modules:Homogenization:DeviceSeed:ResetBeforeImport"] = "false"
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
        Assert.Equal(DeviceType.PLC, device.DeviceType);
        Assert.Equal("Mc", device.DeviceModel);
        Assert.Equal(SeedableSignals().Count, ioMappings.Items.Count);
        Assert.Equal(
            SeedableSignals().Select(static signal => signal.SignalKey).OrderBy(static signalKey => signalKey),
            ioMappings.Items.Select(static mapping => mapping.SignalKey).OrderBy(static signalKey => signalKey));
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "信号交互" && mapping.BusinessGroup == "扫码进站");
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "连续读数据" && mapping.SignalKey == "Homogenization.TrayCode");
        Assert.Contains(ioMappings.Items, static mapping => mapping.Category == "单点读数据" && mapping.BusinessGroup == "实时数据");
        Assert.Contains(ioMappings.Items, static mapping => mapping.SignalKey == "Homogenization.Interaction.Inbound" && mapping.Direction == "Read" && mapping.PlcAddress == "D701");
    }

    [Fact]
    public void HomogenizationModuleConfig_ShouldNotCarryDevelopmentDeviceOrIoSeedJson()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(DependencyInjection).Assembly.Location);
        Assert.False(string.IsNullOrWhiteSpace(assemblyDirectory));

        var configDirectory = Path.Combine(assemblyDirectory!, "Config");
        Assert.False(File.Exists(Path.Combine(configDirectory, "homogenization.io.seed.json")));
        Assert.False(File.Exists(Path.Combine(configDirectory, "homogenization.device.seed.json")));
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenExistingMappingsHaveOldClassification_ShouldKeepUserMappingsWithoutRepair()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Homogenization:DeviceSeed:Enabled"] = "true",
                ["Modules:Homogenization:DeviceSeed:ResetBeforeImport"] = "false"
            })
            .Build();

        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();
        var device = NetworkDeviceEntity.Create("PLC-Homogenization-01", DeviceType.PLC, "127.0.0.1", 6000);
        device.UpdateDeviceModel("Mc");
        device.UpdateEndpoint("127.0.0.1", 6000, null, 3000);
        device.Enable();
        networkDevices.Add(device);

        var legacyMapping = IoMappingEntity.Create(
            device.Id,
            "Homogenization.Interaction.Inbound",
            "D999",
            1,
            "UInt16",
            "Read",
            "单点读数据",
            "单点读数据");
        legacyMapping.UpdateSortOrder(999);
        legacyMapping.UpdateMetadata(
            legacyMapping.SignalKey,
            legacyMapping.DataType,
            legacyMapping.Direction,
            legacyMapping.Category,
            legacyMapping.BusinessGroup,
            "旧分类");
        ioMappings.Add(legacyMapping);

        var contributor = new HomogenizationDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            new FakeLogService(),
            [new HomogenizationHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();

        var preserved = Assert.Single(ioMappings.Items);
        Assert.Equal("D999", preserved.PlcAddress);
        Assert.Equal(1, preserved.AddressCount);
        Assert.Equal("UInt16", preserved.DataType);
        Assert.Equal("Read", preserved.Direction);
        Assert.Equal("单点读数据", preserved.Category);
        Assert.Equal("单点读数据", preserved.BusinessGroup);
        Assert.Equal(999, preserved.SortOrder);
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenGeneratedRuntimeHasLegacyStandardIo_ShouldNotAutoResetExistingMappings()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "homogenization-template-reset-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var configuration = CreateEnabledSeedConfiguration(resetBeforeImport: false);
            var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
            var ioMappings = new InMemoryRepository<IoMappingEntity>();
            var runtimePaths = CreateRuntimePaths(tempDirectory);
            var device = NetworkDeviceEntity.Create("PLC-Homogenization-01", DeviceType.PLC, "127.0.0.1", 6000);
            device.UpdateDeviceModel("Mc");
            device.UpdateEndpoint("127.0.0.1", 6000, null, 3000);
            device.Enable();
            networkDevices.Add(device);

            var oldHeartbeat = IoMappingEntity.Create(device.Id, "Homogenization.HeartbeatIn", "D700", 1, "Int16", "Read", "信号交互", "心跳交互");
            oldHeartbeat.UpdateSortOrder(1);
            ioMappings.Add(oldHeartbeat);
            var oldDebugWrite = IoMappingEntity.Create(device.Id, "TEST", "D801", 1, "Int16", "Write", "信号交互", "111");
            oldDebugWrite.UpdateSortOrder(101);
            ioMappings.Add(oldDebugWrite);

            var contributor = new HomogenizationDevelopmentSampleContributor(
                configuration,
                networkDevices,
                ioMappings,
                new FakeLogService(),
                [new HomogenizationHardwareProfileProvider()],
                runtimePaths);

            await contributor.EnsureConfigurationSamplesAsync();

            Assert.Equal(2, ioMappings.Items.Count);
            Assert.Contains(ioMappings.Items, static mapping => mapping.SignalKey == "Homogenization.HeartbeatIn");
            Assert.Contains(ioMappings.Items, static mapping => mapping.SignalKey == "TEST");
            Assert.DoesNotContain(ioMappings.Items, static mapping => mapping.SignalKey == "Homogenization.Interaction.Heartbeat");
            Assert.False(File.Exists(Path.Combine(runtimePaths.ContextDirectory, "hardware-profile.Homogenization.json")));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenTemplateSignatureUnchanged_ShouldKeepEditedAddress()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "homogenization-template-signature-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var configuration = CreateEnabledSeedConfiguration(resetBeforeImport: false);
            var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
            var ioMappings = new InMemoryRepository<IoMappingEntity>();
            var runtimePaths = CreateRuntimePaths(tempDirectory);
            var contributor = new HomogenizationDevelopmentSampleContributor(
                configuration,
                networkDevices,
                ioMappings,
                new FakeLogService(),
                [new HomogenizationHardwareProfileProvider()],
                runtimePaths);

            await contributor.EnsureConfigurationSamplesAsync();

            var inboundRead = ioMappings.Items.Single(static mapping =>
                mapping.SignalKey == "Homogenization.Interaction.Inbound" && mapping.Direction == "Read");
            inboundRead.UpdateAddress("D999", inboundRead.AddressCount);

            await contributor.EnsureConfigurationSamplesAsync();

            var preserved = ioMappings.Items.Single(static mapping =>
                mapping.SignalKey == "Homogenization.Interaction.Inbound" && mapping.Direction == "Read");
            Assert.Equal("D999", preserved.PlcAddress);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HomogenizationDevelopmentSampleContributor_WhenResetEnabled_ShouldReplaceCurrentPluginDatabaseData()
    {
        var configuration = CreateEnabledSeedConfiguration(resetBeforeImport: true);

        var networkDevices = new InMemoryRepository<NetworkDeviceEntity>();
        var ioMappings = new InMemoryRepository<IoMappingEntity>();

        var otherDevice = NetworkDeviceEntity.Create("PLC-Other", DeviceType.PLC, "10.0.0.2", 102);
        otherDevice.UpdateDeviceModel("S7");
        otherDevice.UpdateEndpoint("10.0.0.2", 102, null, 3000);
        otherDevice.Enable();
        networkDevices.Add(otherDevice);

        var oldHomogenizationDevice = NetworkDeviceEntity.Create("PLC-Old-H", DeviceType.PLC, "10.0.0.3", 6000);
        oldHomogenizationDevice.UpdateDeviceModel("Mc");
        oldHomogenizationDevice.UpdateEndpoint("10.0.0.3", 6000, null, 3000);
        oldHomogenizationDevice.Enable();
        networkDevices.Add(oldHomogenizationDevice);

        var otherMapping = IoMappingEntity.Create(otherDevice.Id, "Other.Signal", "DB1.DBW0", 1, "Int16", "Read");
        otherMapping.UpdateSortOrder(1);
        ioMappings.Add(otherMapping);

        var legacyHomogenizationMapping = IoMappingEntity.Create(oldHomogenizationDevice.Id, "Homogenization.Obsolete", "D0", 1, "Int16", "Read");
        legacyHomogenizationMapping.UpdateSortOrder(1);
        ioMappings.Add(legacyHomogenizationMapping);

        var contributor = new HomogenizationDevelopmentSampleContributor(
            configuration,
            networkDevices,
            ioMappings,
            new FakeLogService(),
            [new HomogenizationHardwareProfileProvider()]);

        await contributor.EnsureConfigurationSamplesAsync();

        var device = Assert.Single(networkDevices.Items);
        Assert.Equal("PLC-Homogenization-01", device.DeviceName);
        Assert.Equal("Mc", device.DeviceModel);
        Assert.DoesNotContain(ioMappings.Items, static mapping => mapping.SignalKey == "Other.Signal");
        Assert.DoesNotContain(ioMappings.Items, static mapping => mapping.SignalKey == "Homogenization.Obsolete");
        Assert.Equal(
            SeedableSignals().Count,
            ioMappings.Items.Count);
    }

    [Fact]
    public async Task HomogenizationStationRuntimeFactory_WhenBindingsReordered_ShouldHandleInboundAndOutboundBySignalKey()
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
                services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>(HomogenizationSignalTestProfile.InteractionProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>(HomogenizationSignalTestProfile.SingleReadProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>(HomogenizationSignalTestProfile.ContinuousReadProfileInstance);
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDeviceService>(deviceService);
        services.AddSingleton<IMesUploadDiagnosticsStore>(diagnostics);
        services.AddSingleton<IHomogenizationMesScenarioChannel>(mesApi);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IProductionTimeProvider>(new FakeProductionTimeProvider());
        services.AddSingleton<IProductionContextSignalBindingStore, ProductionContextSignalBindingStore>();
        services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(new RuntimeFakeModuleParamProvider());
        services.AddSingleton<IHomogenizationProductionGate, AllowAllHomogenizationProductionGate>();
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
            NetworkDeviceId = 7
        };

        provider.GetRequiredService<IProductionContextSignalBindingStore>().Set(context, bindings);

        var readValues = new ushort[GetBufferSize(bindings, "Read")];
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), 7);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 0);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 123);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌电流), 11);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散转速), 222);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散电流), 12);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 26);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), unchecked((ushort)(short)-9));
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT实际值), 15);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT目标值), 18);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNTA罐重量), 2);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNTB罐重量), 3);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP实际值), 27);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP目标值), 30);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料胶液实际值), 31);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料设定搅拌时间), 40);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料剩余搅拌时间), 5);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料设定分散时间), 50);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料剩余分散时间), 6);
        SetAscii(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "TRAY-9001", 30);
        buffer.UpdateReadBuffer(readValues);

        var factory = new HomogenizationStationRuntimeFactory();
        var tasks = factory.CreateTasks(
            provider,
            buffer,
            context,
            factory.GetTaskCandidates().Select(static x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
        using var cancellation = new CancellationTokenSource();
        var runningTasks = tasks.Select(task => task.StartAsync(cancellation.Token)).ToArray();

        await WaitUntilAsync(() =>
            buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳)]] == 7
            && mesApi.LastRealtimeSnapshot is not null
            && context.LastRealtimeSnapshot is not null);
        Assert.Equal((ushort)7, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳)]]);
        Assert.NotNull(mesApi.LastRealtimeSnapshot);
        Assert.Equal(123, mesApi.LastRealtimeSnapshot!.StirringSpeed);
        Assert.Equal(26, context.LastRealtimeSnapshot!.Temperature);
        Assert.Equal(0, context.GetStep("Homogenization.Inbound"));
        Assert.Equal(0, context.GetStep("Homogenization.EquipmentStatus"));
        Assert.Equal(0, context.GetStep("Homogenization.Outbound"));

        mesApi.InboundGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 10);
        await WaitUntilAsync(() => mesApi.InboundTrayCodes.Contains("TRAY-9001"));
        Assert.Equal(10, context.GetStep("Homogenization.Inbound"));
        mesApi.InboundGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 30);

        Assert.Contains("TRAY-9001", mesApi.InboundTrayCodes);
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)]]);

        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Inbound") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)]]);

        mesApi.EquipmentStatusGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 1);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalTrigger);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 10);
        await WaitUntilAsync(() => mesApi.LastEquipmentStatusSnapshot is not null);
        Assert.Equal(10, context.GetStep("Homogenization.EquipmentStatus"));
        mesApi.EquipmentStatusGate.SetResult(true);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 30);

        Assert.NotNull(mesApi.LastEquipmentStatusSnapshot);
        Assert.Equal(1, mesApi.LastEquipmentStatusSnapshot!.StatusCode);
        Assert.Equal("空闲", mesApi.LastEquipmentStatusSnapshot.StatusText);
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)]]);

        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.EquipmentStatus") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传)]]);

        pipeline.EnqueueGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalTrigger);
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
        Assert.Equal(TestCodeOptions.Plc.AckOk, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)]]);

        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Outbound") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传)]]);

        cancellation.Cancel();
        await Task.WhenAll(runningTasks);
    }

    [Fact]
    public async Task HomogenizationStationRuntimeFactory_WhenRecipeBindingsReordered_ShouldDecodeFloatAndArrayValuesBySignalKey()
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
                services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction>>(HomogenizationSignalTestProfile.InteractionProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead>>(HomogenizationSignalTestProfile.SingleReadProfileInstance);
        services.AddSingleton<IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead>>(HomogenizationSignalTestProfile.ContinuousReadProfileInstance);
        services.AddSingleton<ILogService>(logger);
        services.AddSingleton<IDeviceService>(deviceService);
        services.AddSingleton<IMesUploadDiagnosticsStore>(diagnostics);
        services.AddSingleton<IHomogenizationMesScenarioChannel>(mesApi);
        services.AddSingleton<IDataPipelineService>(pipeline);
        services.AddSingleton<IProductionTimeProvider>(new FakeProductionTimeProvider());
        services.AddSingleton<IProductionContextSignalBindingStore, ProductionContextSignalBindingStore>();
        services.AddSingleton<IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>>(new RuntimeFakeModuleParamProvider());
        services.AddSingleton<IHomogenizationProductionGate, AllowAllHomogenizationProductionGate>();
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
            NetworkDeviceId = 8
        };
        provider.GetRequiredService<IProductionContextSignalBindingStore>().Set(context, bindings);

        var readValues = new ushort[GetBufferSize(bindings, "Read")];
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), 3);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), TestCodeOptions.Plc.SignalReset);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), 101);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌电流), 11);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散转速), 202);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散电流), 12);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), 30);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), unchecked((ushort)(short)-12));
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), 0);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), 55);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方分散转速), 66);
        SetFloat(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方NCM), 1, 12.5f);
        SetFloat(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方SP1), 2, 8.75f);
        SetFloat(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方NMP), 0, 99.25f);
        SetFloat(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方胶液), 0, 5.5f);
        SetFloat(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方CNT), 0, 2.25f);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方真空), 1);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方时间), 0, 15);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方温度), 0, 45);
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方停机步), 0, 1);
        buffer.UpdateReadBuffer(readValues);

        var factory = new HomogenizationStationRuntimeFactory();
        var tasks = factory.CreateTasks(
            provider,
            buffer,
            context,
            factory.GetTaskCandidates().Select(static x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase));
        using var cancellation = new CancellationTokenSource();
        var runningTasks = tasks.Select(task => task.StartAsync(cancellation.Token)).ToArray();

        await WaitUntilAsync(() =>
            buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳)]] == 3);
        Assert.Equal(0, context.GetStep("Homogenization.Recipe"));

        mesApi.RecipeGate = NewGate();
        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalTrigger);
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

        SetWord(readValues, readOffsets, HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), TestCodeOptions.Plc.SignalReset);
        buffer.UpdateReadBuffer(readValues);
        await WaitUntilAsync(() => context.GetStep("Homogenization.Recipe") == 0);
        Assert.Equal(TestCodeOptions.Plc.SignalReset, buffer.GetWriteBuffer()[writeOffsets[HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传)]]);

        cancellation.Cancel();
        await Task.WhenAll(runningTasks);
    }

    private static IReadOnlyList<ModuleIoSnapshot> BuildInboundOutboundBindings()
        =>
        [
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT实际值), "D3030", 1, "UInt16", "Read", 1),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), "D701", 1, "Int16", "Read", 2),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.托盘码), "D24500", 30, "Ascii", "Read", 3),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), "D301", 1, "Int16", "Read", 4),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), "D300", 1, "Int16", "Read", 5),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), "D700", 1, "Int16", "Read", 6),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), "D702", 1, "Int16", "Read", 7),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), "D703", 1, "Int16", "Read", 8),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), "D707", 1, "Int16", "Read", 9),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), "D1618", 1, "Int16", "Read", 10),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌电流), "D1616", 1, "Int16", "Read", 11),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散转速), "D1638", 1, "Int16", "Read", 12),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散电流), "D1636", 1, "Int16", "Read", 13),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), "D711", 1, "Int16", "Read", 14),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNT目标值), "D8000", 1, "UInt16", "Read", 15),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNTA罐重量), "D7000", 1, "UInt16", "Read", 16),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料CNTB罐重量), "D7002", 1, "UInt16", "Read", 17),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP实际值), "D812", 1, "UInt16", "Read", 18),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料NMP目标值), "D810", 1, "UInt16", "Read", 19),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料胶液实际值), "D822", 1, "UInt16", "Read", 20),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料设定搅拌时间), "D2054", 1, "UInt16", "Read", 21),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料剩余搅拌时间), "D2056", 1, "UInt16", "Read", 22),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料设定分散时间), "D2044", 1, "UInt16", "Read", 23),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.出料剩余分散时间), "D2046", 1, "UInt16", "Read", 24),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), "D602", 1, "Int16", "Write", 1),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), "D600", 1, "Int16", "Write", 2),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), "D601", 1, "Int16", "Write", 3),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), "D603", 1, "Int16", "Write", 4),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), "D607", 1, "Int16", "Write", 5)
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
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方NMP), "ZR1200", 60, "Float", "Read", 1),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), "D703", 1, "Int16", "Read", 2),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), "D700", 1, "Int16", "Read", 3),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方搅拌转速), "ZR400", 30, "UInt16", "Read", 4),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌转速), "D1618", 1, "Int16", "Read", 5),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站), "D701", 1, "Int16", "Read", 6),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.出料上传), "D702", 1, "Int16", "Read", 7),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.设备状态上传), "D707", 1, "Int16", "Read", 8),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值), "D711", 1, "Int16", "Read", 9),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时搅拌电流), "D1616", 1, "Int16", "Read", 10),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散转速), "D1638", 1, "Int16", "Read", 11),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时分散电流), "D1636", 1, "Int16", "Read", 12),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时温度), "D301", 1, "Int16", "Read", 13),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.实时真空度), "D300", 1, "Int16", "Read", 14),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方分散转速), "ZR500", 30, "UInt16", "Read", 15),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方NCM), "ZR1000", 60, "Float", "Read", 16),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方SP1), "ZR1800", 60, "Float", "Read", 17),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方胶液), "ZR1400", 60, "Float", "Read", 18),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方CNT), "ZR1600", 60, "Float", "Read", 19),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方真空), "R300", 30, "Bool", "Read", 20),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方时间), "ZR0", 30, "UInt16", "Read", 21),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方温度), "ZR100", 30, "Int16", "Read", 22),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.ContinuousRead.配方停机步), "ZR200", 30, "Bool", "Read", 23),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.心跳), "D600", 1, "Int16", "Write", 1),
            new(HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.工艺参数上传), "D603", 1, "Int16", "Write", 2)
        ];

    private static Dictionary<string, int> BuildOffsets(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
    {
        var offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentOffset = 0;

        foreach (var binding in bindings
                     .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(binding => binding.SortOrder))
        {
            offsets[binding.SignalKey] = currentOffset;
            currentOffset += Math.Max(1, binding.AddressCount);
        }

        return offsets;
    }

    private static int GetBufferSize(IReadOnlyList<ModuleIoSnapshot> bindings, string direction)
        => bindings
            .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(binding => binding.SortOrder)
            .Sum(static binding => Math.Max(1, binding.AddressCount));

    private static void SetWord(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string signalKey, ushort value)
        => buffer[offsets[signalKey]] = value;

    private static void SetWord(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string signalKey, int valueIndex, ushort value)
        => buffer[offsets[signalKey] + valueIndex] = value;

    private static void SetAscii(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string signalKey, string value, int wordCount)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        for (var wordIndex = 0; wordIndex < wordCount; wordIndex++)
        {
            var lowIndex = wordIndex * 2;
            var highIndex = lowIndex + 1;
            var low = lowIndex < bytes.Length ? bytes[lowIndex] : (byte)0;
            var high = highIndex < bytes.Length ? bytes[highIndex] : (byte)0;
            buffer[offsets[signalKey] + wordIndex] = (ushort)(low | (high << 8));
        }
    }

    private static void SetFloat(ushort[] buffer, IReadOnlyDictionary<string, int> offsets, string signalKey, int valueIndex, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var baseOffset = offsets[signalKey] + (valueIndex * 2);
        buffer[baseOffset] = (ushort)((bytes[2] << 8) | bytes[3]);
        buffer[baseOffset + 1] = (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static TaskCompletionSource<bool> NewGate()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IConfiguration CreateEnabledSeedConfiguration(bool resetBeforeImport)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:Homogenization:DeviceSeed:Enabled"] = "true",
                ["Modules:Homogenization:DeviceSeed:ResetBeforeImport"] = resetBeforeImport.ToString()
            })
            .Build();

    private static EdgeRuntimePaths CreateRuntimePaths(string runtimeRoot)
        => new(
            BaseDirectory: runtimeRoot,
            ProfileName: "HomogenizationLine",
            RuntimeDataRoot: runtimeRoot,
            DatabaseDirectory: Path.Combine(runtimeRoot, "db"),
            ContextDirectory: Path.Combine(runtimeRoot, "context"),
            RecipeDirectory: Path.Combine(runtimeRoot, "recipe"),
            ExcelDirectory: Path.Combine(runtimeRoot, "excel"),
            DiagnosticsDirectory: Path.Combine(runtimeRoot, "diagnostics"),
            LogDirectory: Path.Combine(runtimeRoot, "diagnostics", "logs"),
            DeviceCacheFilePath: Path.Combine(runtimeRoot, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(runtimeRoot, "diagnostics", "crash.log"),
            FallbackCrashLogPath: Path.Combine(runtimeRoot, "diagnostics", "crash.fallback.log"));

    private static IReadOnlyList<HomogenizationTestSignalDefinition> SeedableSignals()
        => HomogenizationSignalTestProfile.Signals
            .Where(static signal => !string.IsNullOrWhiteSpace(signal.DefaultAddress))
            .ToArray();

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
        : IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>
    {
        public Task<ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>(
                "Homogenization",
                EmptyGroup<HomogenizationParams.Mes>(ModuleParamCategory.Mes),
                EmptyGroup<HomogenizationParams.Cloud>(ModuleParamCategory.Cloud),
                new ModuleParamGroup<HomogenizationParams.Business>(
                    "Homogenization",
                    ModuleParamCategory.Business,
                    new Dictionary<HomogenizationParams.Business, string>(),
                    new Dictionary<HomogenizationParams.Business, string?>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = "false"
                    },
                    new Dictionary<HomogenizationParams.Business, ParamValueKind>
                    {
                        [HomogenizationParams.Business.启用托盘码重码验证] = ParamValueKind.Bool
                    },
                    warn: null)));

        private static ModuleParamGroup<TEnum> EmptyGroup<TEnum>(ModuleParamCategory category)
            where TEnum : struct, Enum
            => new(
                "Homogenization",
                category,
                new Dictionary<TEnum, string>(),
                new Dictionary<TEnum, string?>(),
                new Dictionary<TEnum, ParamValueKind>(),
                warn: null);
    }

    private sealed class CapturingHomogenizationMesChannel : IHomogenizationMesScenarioChannel
    {
        public List<string> InboundTrayCodes { get; } = [];

        public string ProcessType => "Homogenization";

        public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

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

        public Task<MesCallResult<HomogenizationMainPlan>> GetMainPlanAsync(
            HomogenizationMainPlanRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationMainPlan>.Success(new HomogenizationMainPlan([])));

        public Task<MesCallResult<HomogenizationTraceBatchResult>> GenerateTraceBatchNumberAsync(
            HomogenizationTraceBatchRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult<HomogenizationTraceBatchResult>.Success(null));

        public Task<MesCallResult> UploadAsync(
            ProcessUploadContext context,
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

    private sealed class AllowAllHomogenizationProductionGate : IHomogenizationProductionGate
    {
        public Task<MesCallResult> EnsureReadyAsync(
            HomogenizationContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success("测试门禁通过。"));
    }
}
