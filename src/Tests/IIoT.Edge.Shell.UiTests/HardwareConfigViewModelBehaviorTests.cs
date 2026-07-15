using System.Globalization;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Shell.UiTests;

public sealed class HardwareConfigViewModelBehaviorTests
{
    [AvaloniaFact]
    public async Task AddNetworkDevice_WhenConfirmed_ShouldSaveImmediately()
    {
        var viewModel = CreateViewModel();

        viewModel.AddNetworkDeviceCommand.Execute(null);

        Assert.True(viewModel.IsNetworkDeviceDialogOpen);
        Assert.Empty(viewModel.NetworkDevices);
        Assert.NotNull(viewModel.EditingNetworkDevice);

        viewModel.EditingNetworkDevice!.DeviceName = "PLC-01";
        viewModel.EditingNetworkDevice.IpAddress = "192.168.1.10";
        viewModel.EditingNetworkDevice.Port1 = 102;
        viewModel.ConfirmNetworkDeviceDialogCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.NetworkDevices.Count == 1 && !viewModel.IsNetworkDeviceDialogOpen);

        var added = Assert.Single(viewModel.NetworkDevices);
        Assert.Equal("PLC-01", added.DeviceName);
        Assert.False(viewModel.IsNetworkDeviceDialogOpen);
    }

    [AvaloniaFact]
    public async Task AddNetworkDevice_WhenSaveFails_ShouldReloadPersistedSnapshot()
    {
        var service = new StubHardwareConfigCrudService
        {
            InitialNetworkDevices =
            [
                CreateNetworkDeviceDto(1, "PLC-01", DeviceType.PLC)
            ],
            SaveResult = CrudOperationResult.Failure("数据库保存失败。")
        };
        var viewModel = CreateViewModel(service);
        await viewModel.OnActivatedAsync();

        viewModel.AddNetworkDeviceCommand.Execute(null);
        viewModel.EditingNetworkDevice!.DeviceName = "PLC-FAIL";
        viewModel.EditingNetworkDevice.IpAddress = "192.168.1.20";
        viewModel.EditingNetworkDevice.Port1 = 102;
        viewModel.ConfirmNetworkDeviceDialogCommand.Execute(null);

        await WaitUntilAsync(() => viewModel.HasError
                                   && viewModel.NetworkDevices.Count == 1
                                   && viewModel.NetworkDevices[0].DeviceName == "PLC-01");

        var current = Assert.Single(viewModel.NetworkDevices);
        Assert.Equal("PLC-01", current.DeviceName);
        Assert.False(viewModel.IsNetworkDeviceDialogOpen);
        Assert.Contains("数据库保存失败", viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task EditIoMapping_WhenConfirmed_ShouldUpdateSelectedMapping()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var mapping = CreateMapping(
            "TestPlugin.RealtimeTemperature",
            "D300",
            IoMappingOptionCatalog.DirectionRead,
            "实时数据",
            IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(mapping);
        viewModel.RefreshIoMappingGroups();
        viewModel.SelectedIoMapping = mapping;

        viewModel.OpenEditIoMappingDialogCommand.Execute(null);

        Assert.True(viewModel.IsEditIoMappingDialogOpen);
        Assert.NotNull(viewModel.EditingIoMapping);
        viewModel.EditingIoMapping!.PlcAddress = "D301";
        viewModel.ConfirmEditIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Any(x => x.PlcAddress == "D301"));

        Assert.Contains(viewModel.IoMappings, x => x.PlcAddress == "D301");
        Assert.False(viewModel.IsEditIoMappingDialogOpen);
    }

    [AvaloniaFact]
    public async Task LoadAll_WhenSelectionIsAll_ShouldExposePlcsWithoutAutoSelectingFirstDevice()
    {
        var service = new StubHardwareConfigCrudService
        {
            InitialNetworkDevices =
            [
                CreateNetworkDeviceDto(1, "Scanner-01", DeviceType.Scanner),
                CreateNetworkDeviceDto(2, "PLC-01", DeviceType.PLC)
            ]
        };
        var viewModel = CreateViewModel(service);

        await viewModel.OnActivatedAsync();

        var plc = Assert.Single(viewModel.IoMappingNetworkDevices);
        Assert.Equal(DeviceType.PLC, plc.DeviceType);
        Assert.Null(viewModel.SelectedNetworkDevice);
        Assert.True(viewModel.ShouldShowIoMappingDeviceSelectionPrompt);
    }

    [AvaloniaFact]
    public async Task LoadAll_WhenSharedSelectionMatchesPlc_ShouldSelectThatDeviceForIoMapping()
    {
        var service = new StubHardwareConfigCrudService
        {
            InitialNetworkDevices =
            [
                CreateNetworkDeviceDto(1, "PLC-01", DeviceType.PLC),
                CreateNetworkDeviceDto(2, "PLC-02", DeviceType.PLC)
            ]
        };
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("PLC-02");
        var viewModel = CreateViewModel(service, selectionService);

        await viewModel.OnActivatedAsync();

        Assert.Equal("PLC-02", viewModel.SelectedNetworkDevice?.DeviceName);
        Assert.False(viewModel.ShouldShowIoMappingDeviceSelectionPrompt);
    }

    [AvaloniaFact]
    public async Task AddInteraction_WhenStandardGroupSelected_ShouldAddReadAndWriteTogether()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        viewModel.StandardInteractionGroups.Add(new IoStandardSignalGroupOptionVm(
            "扫码进站",
            [
                new ModuleIoTemplateEntry(
                    "TestPlugin.Interaction.Inbound",
                    "D701",
                    1,
                    "Int16",
                    "Read",
                    2,
                    "进站触发",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "扫码进站"),
                new ModuleIoTemplateEntry(
                    "TestPlugin.Interaction.Inbound",
                    "D601",
                    1,
                    "Int16",
                    "Write",
                    102,
                    "进站应答",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "扫码进站")
            ]));

        viewModel.OpenAddInteractionMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewInteractionPair);
        Assert.Equal("扫码进站", viewModel.NewInteractionPair!.BusinessGroup);
        viewModel.NewInteractionPair.ReadAddressCount = 2;
        viewModel.NewInteractionPair.WriteAddressCount = 3;
        viewModel.NewInteractionPair.Remark = "成对备注";
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 2);

        Assert.Equal(2, viewModel.IoMappings.Count);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "TestPlugin.Interaction.Inbound" && x.Direction == "Read" && x.AddressCount == 2);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "TestPlugin.Interaction.Inbound" && x.Direction == "Write" && x.AddressCount == 3);
        Assert.All(viewModel.IoMappings, x => Assert.Equal("扫码进站", x.BusinessGroup));
        Assert.All(viewModel.IoMappings, x => Assert.Equal("成对备注", x.Remark));
    }

    [AvaloniaFact]
    public async Task AddInteraction_WhenEnumCandidateHasNoSeedMetadata_ShouldRequireManualAddresses()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        viewModel.StandardInteractionGroups.Add(new IoStandardSignalGroupOptionVm(
            "手工地址测试",
            [
                new ModuleIoTemplateEntry(
                    "Test.Interaction.Manual",
                    string.Empty,
                    1,
                    "Int16",
                    "Read",
                    901,
                    "手工地址测试读点",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "手工地址测试"),
                new ModuleIoTemplateEntry(
                    "Test.Interaction.Manual",
                    string.Empty,
                    1,
                    "Int16",
                    "Write",
                    902,
                    "手工地址测试写点",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "手工地址测试")
            ]));

        viewModel.OpenAddInteractionMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewInteractionPair);
        Assert.Equal("手工地址测试", viewModel.NewInteractionPair!.BusinessGroup);
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        Assert.Empty(viewModel.IoMappings);
        Assert.Contains("读地址和写地址", viewModel.ErrorMessage);

        viewModel.NewInteractionPair.ReadPlcAddress = "D300";
        viewModel.NewInteractionPair.WritePlcAddress = "D200";
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 2);

        Assert.Equal(2, viewModel.IoMappings.Count);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Test.Interaction.Manual" && x.Direction == "Read" && x.PlcAddress == "D300");
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Test.Interaction.Manual" && x.Direction == "Write" && x.PlcAddress == "D200");
    }

    [AvaloniaFact]
    public async Task AddDataPoint_WhenStandardDataPointSelected_ShouldGenerateSingleReadMapping()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.RealtimeTemperature",
            "D301",
            1,
            "Int16",
            "Read",
            9,
            "实时温度",
            IoMappingOptionCatalog.CategorySingleRead,
            "实时数据")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        viewModel.NewIoMapping!.PlcAddress = "D900";
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.DirectionWrite;

        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 1);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal("TestPlugin.RealtimeTemperature", mapping.SignalKey);
        Assert.Equal("D900", mapping.PlcAddress);
        Assert.Equal("实时数据", mapping.BusinessGroup);
        Assert.Equal("Read", mapping.Direction);
        Assert.Equal(1, mapping.AddressCount);
    }

    [AvaloniaFact]
    public async Task AddDataPoint_WhenSingleWriteSelected_ShouldKeepEditableCount()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.Debug.SingleWrite",
            "D200",
            1,
            "Int16",
            "Write",
            201,
            "单点写入",
            IoMappingOptionCatalog.CategorySingleWrite,
            "写入数据")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        viewModel.NewIoMapping!.AddressCount = 9;
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 1);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal(IoMappingOptionCatalog.CategorySingleWrite, mapping.Category);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, mapping.Direction);
        Assert.Equal(9, mapping.AddressCount);
    }

    [AvaloniaFact]
    public async Task EditInteractionPair_WhenConfirmed_ShouldUpdateReadAndWriteTogether()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var read = CreateMapping("TestPlugin.Interaction.Heartbeat", "D700", "Read", "心跳");
        var write = CreateMapping("TestPlugin.Interaction.Heartbeat", "D600", "Write", "心跳");
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.RefreshIoMappingGroups();

        var pair = Assert.Single(viewModel.InteractionIoMappingPairs);
        Assert.Equal("心跳", pair.BusinessGroup);
        viewModel.SelectedInteractionPair = pair;

        viewModel.OpenEditIoMappingDialogCommand.Execute(null);

        Assert.True(viewModel.IsEditIoMappingDialogOpen);
        Assert.Null(viewModel.EditingIoMapping);
        Assert.NotNull(viewModel.EditingInteractionPair);
        Assert.Equal("编辑信号交互 - 心跳", viewModel.IoMappingEditDialogTitle);

        viewModel.EditingInteractionPair!.ReadPlcAddress = "D710";
        viewModel.EditingInteractionPair.ReadAddressCount = 2;
        viewModel.EditingInteractionPair.ReadDataType = IoMappingOptionCatalog.DataTypeUInt16;
        viewModel.EditingInteractionPair.WritePlcAddress = "D610";
        viewModel.EditingInteractionPair.WriteAddressCount = 3;
        viewModel.EditingInteractionPair.WriteDataType = IoMappingOptionCatalog.DataTypeInt32;
        viewModel.EditingInteractionPair.Remark = "现场调整";
        viewModel.ConfirmEditIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Any(x => x.PlcAddress == "D710")
                                   && service.SavedMappings.Any(x => x.PlcAddress == "D610"));

        Assert.Contains(viewModel.IoMappings, x => x.PlcAddress == "D710" && x.AddressCount == 2 && x.DataType == IoMappingOptionCatalog.DataTypeUInt16);
        Assert.Contains(viewModel.IoMappings, x => x.PlcAddress == "D610" && x.AddressCount == 3 && x.DataType == IoMappingOptionCatalog.DataTypeInt32);
        Assert.All(viewModel.IoMappings, x => Assert.Equal("现场调整", x.Remark));
        Assert.False(viewModel.IsEditIoMappingDialogOpen);
    }

    [AvaloniaFact]
    public async Task AddDataPoint_WhenContinuousWriteSelected_ShouldKeepEditableCount()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.Debug.ContinuousWrite",
            "D220",
            8,
            "UInt16",
            "Write",
            202,
            "连续写入",
            IoMappingOptionCatalog.CategoryContinuousWrite,
            "写入数据")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        Assert.True(viewModel.NewIoMapping!.IsAddressCountEditable);
        viewModel.NewIoMapping.AddressCount = 12;
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 1);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal(IoMappingOptionCatalog.CategoryContinuousWrite, mapping.Category);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, mapping.Direction);
        Assert.Equal(12, mapping.AddressCount);
    }

    [AvaloniaFact]
    public void AddDataPoint_WhenCategoryChanged_ShouldSwitchToSameCategoryStandardSignal()
    {
        var viewModel = CreateViewModel();
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.RealtimeTemperature",
            "D301",
            1,
            "Int16",
            "Read",
            9,
            "实时温度",
            IoMappingOptionCatalog.CategorySingleRead,
            "实时数据")));
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.Debug.SingleWrite",
            "D200",
            1,
            "Int16",
            "Write",
            201,
            "单点写入",
            IoMappingOptionCatalog.CategorySingleWrite,
            "写入数据")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        viewModel.NewIoMapping!.Category = IoMappingOptionCatalog.CategorySingleWrite;

        Assert.Equal("TestPlugin.Debug.SingleWrite", viewModel.SelectedStandardIoSignal?.SignalKey);
    }

    [AvaloniaFact]
    public void AddDataPoint_WhenSingleWriteHasNoCandidate_ShouldNotFallbackToReadTemplate()
    {
        var viewModel = CreateViewModel();
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.DeviceStatus",
            "D711",
            1,
            "Int16",
            "Read",
            30,
            "状态值",
            IoMappingOptionCatalog.CategorySingleRead,
            "设备状态")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        viewModel.NewIoMapping!.Category = IoMappingOptionCatalog.CategorySingleWrite;

        Assert.Null(viewModel.SelectedStandardIoSignal);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, viewModel.NewIoMapping.Direction);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.PlcAddress);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.BusinessGroup);

        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        Assert.Empty(viewModel.IoMappings);
        Assert.Contains("当前分类暂无插件枚举信号", viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public void AddDataPoint_WhenContinuousWriteHasNoCandidate_ShouldNotFallbackToReadTemplate()
    {
        var viewModel = CreateViewModel();
        SelectPersistedPlc(viewModel);
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "TestPlugin.DeviceStatus",
            "D711",
            1,
            "Int16",
            "Read",
            30,
            "状态值",
            IoMappingOptionCatalog.CategorySingleRead,
            "设备状态")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        viewModel.NewIoMapping!.Category = IoMappingOptionCatalog.CategoryContinuousWrite;

        Assert.Null(viewModel.SelectedStandardIoSignal);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, viewModel.NewIoMapping.Direction);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.PlcAddress);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.BusinessGroup);

        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        Assert.Empty(viewModel.IoMappings);
        Assert.Contains("当前分类暂无插件枚举信号", viewModel.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task Save_WhenCategoryDirectionMismatch_ShouldFailValidation()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var mapping = CreateMapping(
            "TestPlugin.Debug.SingleWrite",
            "D200",
            IoMappingOptionCatalog.DirectionRead,
            "写入数据",
            IoMappingOptionCatalog.CategorySingleWrite);
        mapping.Direction = IoMappingOptionCatalog.DirectionRead;
        viewModel.IoMappings.Add(mapping);

        var saveMethod = typeof(HardwareConfigViewModel).GetMethod(
            "SaveAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var saveTask = Assert.IsType<Task<CrudOperationResult>>(saveMethod!.Invoke(viewModel, null));
        var result = await saveTask;

        Assert.False(result.IsSuccess);
        Assert.Contains("请先修正无效表单字段", result.Message);
        Assert.Empty(service.SavedMappings);
    }

    [AvaloniaFact]
    public async Task DeleteIoPoint_WhenInteractionSelected_ShouldDeleteWholeBusinessGroup()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var read = CreateMapping("TestPlugin.Interaction.Recipe", "D703", "Read", "工艺参数上传");
        var write = CreateMapping("TestPlugin.Interaction.Recipe", "D603", "Write", "工艺参数上传");
        var data = CreateMapping("TestPlugin.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = read;
        viewModel.DeleteIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 1);

        var remain = Assert.Single(viewModel.IoMappings);
        Assert.Equal("TestPlugin.RealtimeTemperature", remain.SignalKey);
    }

    [AvaloniaFact]
    public async Task DeleteIoPoint_WhenLegacyInteractionSelected_ShouldDeleteWholeLegacyGroup()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var legacy = CreateMapping("TEST", "D801", "Write", "111");
        var data = CreateMapping("TestPlugin.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(legacy);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = legacy;
        viewModel.DeleteIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count == 1);

        var remain = Assert.Single(viewModel.IoMappings);
        Assert.Equal("TestPlugin.RealtimeTemperature", remain.SignalKey);
    }

    [AvaloniaFact]
    public async Task DeleteInteraction_WhenConfirmed_ShouldSubmitOnlyRemainingMappings()
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        SelectPersistedPlc(viewModel);
        var read = CreateMapping("TestPlugin.Interaction.Outbound", "D702", "Read", "出料上传");
        var write = CreateMapping("TestPlugin.Interaction.Outbound", "D602", "Write", "出料上传");
        var data = CreateMapping("TestPlugin.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = read;
        viewModel.DeleteIoMappingCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count > 0);

        var saved = Assert.Single(service.SavedMappings);
        Assert.Equal("TestPlugin.RealtimeTemperature", saved.SignalKey);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static NetworkDeviceVm SelectPersistedPlc(HardwareConfigViewModel viewModel)
    {
        var plc = CreatePlc();
        viewModel.NetworkDevices.Add(plc);
        viewModel.RefreshIoMappingNetworkDevices();
        viewModel.SelectedNetworkDevice = plc;
        return plc;
    }

    private static HardwareConfigViewModel CreateViewModel(
        StubHardwareConfigCrudService? service = null,
        IDeviceSelectionService? selectionService = null)
    {
        var languageService = new TestLanguageService();
        var validationPresenter = new HardwareConfigValidationPresenter(
            new NetworkDeviceValidator(languageService),
            new SerialDeviceValidator(languageService),
            new IoMappingValidator(languageService));
        var editSession = new HardwareConfigEditSession(
            validationPresenter,
            new HardwareConfigStandardSignalDraftService(),
            new HardwareConfigMappingSaveBuilder());
        return new HardwareConfigViewModel(
            new StubPermissionService { CanEditHardware = true },
            languageService,
            new HardwareConfigLoadSaveCoordinator(
                service ?? new StubHardwareConfigCrudService(),
                validationPresenter,
                editSession,
                new HardwareConfigEditModelMapper(),
                languageService),
            new HardwareConfigDeviceSelectionCoordinator(),
            editSession,
            selectionService ?? new DeviceSelectionService());
    }

    private static NetworkDeviceVm CreatePlc()
        => new()
        {
            Id = 7,
            DeviceName = "PLC-TestPlugin-01",
            DeviceType = DeviceType.PLC,
            DeviceModel = PlcType.Mc.ToString(),
            IpAddress = "192.168.1.10",
            Port1 = 102,
            ConnectTimeout = 3000,
            IsEnabled = true
        };

    private static NetworkDeviceDto CreateNetworkDeviceDto(
        int id,
        string deviceName,
        DeviceType deviceType)
        => new(
            id,
            deviceName,
            deviceType,
            deviceType == DeviceType.PLC ? PlcType.Mc.ToString() : null,
            "192.168.1.10",
            102,
            null,
            null,
            null,
            3000,
            true,
            null);

    private static IoMappingVm CreateMapping(
        string signalKey,
        string address,
        string direction,
        string businessGroup,
        string category = IoMappingOptionCatalog.CategoryInteraction)
        => new()
        {
            NetworkDeviceId = 7,
            SignalKey = signalKey,
            PlcAddress = address,
            AddressCount = 1,
            DataType = "Int16",
            Direction = direction,
            Category = category,
            BusinessGroup = businessGroup,
            SortOrder = 1
        };

    private sealed class StubHardwareConfigCrudService : IHardwareConfigCrudService
    {
        private List<NetworkDeviceDto>? _networkDevices;
        private List<SerialDeviceDto>? _serialDevices;
        private readonly Dictionary<int, List<IoMappingDto>> _ioMappingsByDeviceId = new();

        public IReadOnlyCollection<NetworkDeviceDto> InitialNetworkDevices { get; init; } = [];

        public IReadOnlyCollection<SerialDeviceDto> InitialSerialDevices { get; init; } = [];

        public IReadOnlyCollection<NetworkDeviceDto> SavedNetworkDevices { get; private set; } = [];

        public IReadOnlyCollection<SerialDeviceDto> SavedSerialDevices { get; private set; } = [];

        public IReadOnlyCollection<IoMappingDto> SavedMappings { get; private set; } = [];

        public CrudOperationResult SaveResult { get; init; } = CrudOperationResult.Success("测试");

        public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            _networkDevices ??= InitialNetworkDevices.ToList();
            _serialDevices ??= InitialSerialDevices.ToList();

            return Task.FromResult(new HardwareConfigInitResult(
                _networkDevices.ToList(),
                _serialDevices.ToList()));
        }

        public Task<IoMappingPageResult> LoadIoMappingsAsync(int networkDeviceId, CancellationToken cancellationToken = default)
        {
            var mappings = _ioMappingsByDeviceId.TryGetValue(networkDeviceId, out var items)
                ? items.ToList()
                : [];

            return Task.FromResult(new IoMappingPageResult(mappings, mappings.Count));
        }

        public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
            NetworkDeviceDto? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleTemplateInfoResult(false, [], [], "测试标准点位。"));

        public Task<CrudOperationResult> ApplyModuleTemplateAsync(
            NetworkDeviceDto? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CrudOperationResult.Success("测试"));

        public Task<CrudOperationResult> SaveAsync(
            IReadOnlyCollection<NetworkDeviceDto> networkDevices,
            IReadOnlyCollection<SerialDeviceDto> serialDevices,
            int selectedNetworkDeviceId,
            IReadOnlyCollection<IoMappingDto> ioMappings,
            CancellationToken cancellationToken = default)
        {
            if (!SaveResult.IsSuccess)
            {
                return Task.FromResult(SaveResult);
            }

            _networkDevices = networkDevices.ToList();
            _serialDevices = serialDevices.ToList();
            SavedNetworkDevices = _networkDevices.ToArray();
            SavedSerialDevices = _serialDevices.ToArray();
            SavedMappings = ioMappings.ToArray();
            if (selectedNetworkDeviceId > 0)
            {
                _ioMappingsByDeviceId[selectedNetworkDeviceId] = ioMappings.ToList();
            }

            return Task.FromResult(SaveResult);
        }
    }

    private sealed class StubPermissionService : IClientPermissionService
    {
        public bool CanEditParams { get; init; }

        public bool CanEditHardware { get; init; }

        public bool IsLocalAdmin { get; init; }

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => CanEditHardware;
    }

    private sealed class TestLanguageService : IAppLanguageService
    {
        public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

        public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == Current.Name);

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
            new(CultureInfo.GetCultureInfo("en-US"), "English")
        ];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void Change(CultureInfo culture)
        {
            Current = culture;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string fallback = "") => fallback;

        public string Format(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, fallback, args);
    }
}
