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
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HardwareConfigViewModelBehaviorTests
{
    [AvaloniaFact]
    public Task AddNetworkDevice_WhenConfirmed_ShouldAddDraftInsteadOfInlineRow()
        => RunOnStaThreadAsync(() =>
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

        var added = Assert.Single(viewModel.NetworkDevices);
        Assert.Equal("PLC-01", added.DeviceName);
        Assert.False(viewModel.IsNetworkDeviceDialogOpen);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task EditIoMapping_WhenConfirmed_ShouldUpdateSelectedMapping()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        var mapping = CreateMapping(
            "Homogenization.RealtimeTemperature",
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

        Assert.Equal("D301", mapping.PlcAddress);
        Assert.False(viewModel.IsEditIoMappingDialogOpen);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task LoadAll_WhenNetworkDevicesIncludeNonPlc_ShouldExposeOnlyPlcsForIoMapping()
        => RunOnStaThreadAsync(async () =>
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
        Assert.Equal("PLC-01", viewModel.SelectedNetworkDevice?.DeviceName);
    });

    [AvaloniaFact]
    public Task AddInteraction_WhenStandardGroupSelected_ShouldAddReadAndWriteTogether()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardInteractionGroups.Add(new IoStandardSignalGroupOptionVm(
            "扫码进站",
            [
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.Inbound",
                    "D701",
                    1,
                    "Int16",
                    "Read",
                    2,
                    "进站触发",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "扫码进站"),
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.Inbound",
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

        Assert.Equal(2, viewModel.IoMappings.Count);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.Inbound" && x.Direction == "Read" && x.AddressCount == 2);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.Inbound" && x.Direction == "Write" && x.AddressCount == 3);
        Assert.All(viewModel.IoMappings, x => Assert.Equal("扫码进站", x.BusinessGroup));
        Assert.All(viewModel.IoMappings, x => Assert.Equal("成对备注", x.Remark));
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddInteraction_WhenEnumCandidateHasNoSeedMetadata_ShouldRequireManualAddresses()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardInteractionGroups.Add(new IoStandardSignalGroupOptionVm(
            "test1",
            [
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.test1",
                    string.Empty,
                    1,
                    "Int16",
                    "Read",
                    10005,
                    "test1 读点",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "test1"),
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.test1",
                    string.Empty,
                    1,
                    "Int16",
                    "Write",
                    20005,
                    "test1 写点",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "test1")
            ]));

        viewModel.OpenAddInteractionMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewInteractionPair);
        Assert.Equal("test1", viewModel.NewInteractionPair!.BusinessGroup);
        viewModel.ConfirmAddIoMappingCommand.Execute(null);
        Assert.Empty(viewModel.IoMappings);
        Assert.Contains("读地址和写地址", viewModel.ErrorMessage);

        viewModel.NewInteractionPair.ReadPlcAddress = "D300";
        viewModel.NewInteractionPair.WritePlcAddress = "D200";
        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        Assert.Equal(2, viewModel.IoMappings.Count);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.test1" && x.Direction == "Read" && x.PlcAddress == "D300");
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.test1" && x.Direction == "Write" && x.PlcAddress == "D200");
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenStandardDataPointSelected_ShouldGenerateSingleReadMapping()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.RealtimeTemperature",
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

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", mapping.SignalKey);
        Assert.Equal("D900", mapping.PlcAddress);
        Assert.Equal("实时数据", mapping.BusinessGroup);
        Assert.Equal("Read", mapping.Direction);
        Assert.Equal(1, mapping.AddressCount);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenSingleWriteSelected_ShouldKeepEditableCount()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.Debug.SingleWrite",
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

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal(IoMappingOptionCatalog.CategorySingleWrite, mapping.Category);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, mapping.Direction);
        Assert.Equal(9, mapping.AddressCount);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task EditInteractionPair_WhenConfirmed_ShouldUpdateReadAndWriteTogether()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        var read = CreateMapping("Homogenization.Interaction.Heartbeat", "D700", "Read", "心跳");
        var write = CreateMapping("Homogenization.Interaction.Heartbeat", "D600", "Write", "心跳");
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

        Assert.Equal("D710", read.PlcAddress);
        Assert.Equal(2, read.AddressCount);
        Assert.Equal(IoMappingOptionCatalog.DataTypeUInt16, read.DataType);
        Assert.Equal("D610", write.PlcAddress);
        Assert.Equal(3, write.AddressCount);
        Assert.Equal(IoMappingOptionCatalog.DataTypeInt32, write.DataType);
        Assert.Equal("现场调整", read.Remark);
        Assert.Equal("现场调整", write.Remark);
        Assert.False(viewModel.IsEditIoMappingDialogOpen);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenContinuousWriteSelected_ShouldKeepEditableCount()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.Debug.ContinuousWrite",
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

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal(IoMappingOptionCatalog.CategoryContinuousWrite, mapping.Category);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, mapping.Direction);
        Assert.Equal(12, mapping.AddressCount);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenCategoryChanged_ShouldSwitchToSameCategoryStandardSignal()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.RealtimeTemperature",
            "D301",
            1,
            "Int16",
            "Read",
            9,
            "实时温度",
            IoMappingOptionCatalog.CategorySingleRead,
            "实时数据")));
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.Debug.SingleWrite",
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

        Assert.Equal("Homogenization.Debug.SingleWrite", viewModel.SelectedStandardIoSignal?.SignalKey);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenSingleWriteHasNoCandidate_ShouldNotFallbackToReadTemplate()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.DeviceStatus",
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
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenContinuousWriteHasNoCandidate_ShouldNotFallbackToReadTemplate()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.DeviceStatus",
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
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task Save_WhenCategoryDirectionMismatch_ShouldFailValidation()
        => RunOnStaThreadAsync(async () =>
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        viewModel.SelectedNetworkDevice = CreatePlc();
        var mapping = CreateMapping(
            "Homogenization.Debug.SingleWrite",
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
    });

    [AvaloniaFact]
    public Task DeleteIoPoint_WhenInteractionSelected_ShouldDeleteWholeBusinessGroup()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        var read = CreateMapping("Homogenization.Interaction.Recipe", "D703", "Read", "工艺参数上传");
        var write = CreateMapping("Homogenization.Interaction.Recipe", "D603", "Write", "工艺参数上传");
        var data = CreateMapping("Homogenization.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = read;
        viewModel.DeleteIoMappingCommand.Execute(null);

        var remain = Assert.Single(viewModel.IoMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", remain.SignalKey);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task DeleteIoPoint_WhenLegacyInteractionSelected_ShouldDeleteWholeLegacyGroup()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        var legacy = CreateMapping("TEST", "D801", "Write", "111");
        var data = CreateMapping("Homogenization.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(legacy);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = legacy;
        viewModel.DeleteIoMappingCommand.Execute(null);

        var remain = Assert.Single(viewModel.IoMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", remain.SignalKey);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task Save_WhenInteractionDeleted_ShouldSubmitOnlyRemainingMappings()
        => RunOnStaThreadAsync(async () =>
    {
        var service = new StubHardwareConfigCrudService();
        var viewModel = CreateViewModel(service);
        viewModel.SelectedNetworkDevice = CreatePlc();
        var read = CreateMapping("Homogenization.Interaction.Outbound", "D702", "Read", "出料上传");
        var write = CreateMapping("Homogenization.Interaction.Outbound", "D602", "Write", "出料上传");
        var data = CreateMapping("Homogenization.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.IoMappings.Add(data);

        viewModel.SelectedIoMapping = read;
        viewModel.DeleteIoMappingCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => service.SavedMappings.Count > 0);

        var saved = Assert.Single(service.SavedMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", saved.SignalKey);
    });

    private static Task RunOnStaThreadAsync(Func<Task> action) => action();

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private static HardwareConfigViewModel CreateViewModel(StubHardwareConfigCrudService? service = null)
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
            editSession);
    }

    private static NetworkDeviceVm CreatePlc()
        => new()
        {
            Id = 7,
            DeviceName = "PLC-Homogenization-01",
            DeviceType = DeviceType.PLC
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
        public IReadOnlyCollection<NetworkDeviceDto> InitialNetworkDevices { get; init; } = [];

        public IReadOnlyCollection<SerialDeviceDto> InitialSerialDevices { get; init; } = [];

        public IReadOnlyCollection<IoMappingDto> SavedMappings { get; private set; } = [];

        public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HardwareConfigInitResult(InitialNetworkDevices.ToList(), InitialSerialDevices.ToList()));

        public Task<IoMappingPageResult> LoadIoMappingsAsync(int networkDeviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IoMappingPageResult([], 0));

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
            SavedMappings = ioMappings.ToArray();
            return Task.FromResult(CrudOperationResult.Success("测试"));
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
