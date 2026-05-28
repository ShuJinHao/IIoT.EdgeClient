using System.Globalization;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HardwareConfigViewModelBehaviorTests
{
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
                    "扫码进站",
                    "PLC 触发"),
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.Inbound",
                    "D601",
                    1,
                    "Int16",
                    "Write",
                    102,
                    "进站应答",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "扫码进站",
                    "上位机应答")
            ]));

        viewModel.OpenAddInteractionMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewInteractionPair);
        Assert.True(viewModel.NewInteractionPair!.IsStandardSource);
        Assert.Equal("扫码进站", viewModel.NewInteractionPair.BusinessGroup);
        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        Assert.Equal(2, viewModel.IoMappings.Count);
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.Inbound" && x.Direction == "Read");
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.Interaction.Inbound" && x.Direction == "Write");
        Assert.All(viewModel.IoMappings, x => Assert.Equal("扫码进站", x.BusinessGroup));
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
                    "test1",
                    "PLC 触发"),
                new ModuleIoTemplateEntry(
                    "Homogenization.Interaction.test1",
                    string.Empty,
                    1,
                    "Int16",
                    "Write",
                    20005,
                    "test1 写点",
                    IoMappingOptionCatalog.CategoryInteraction,
                    "test1",
                    "上位机应答")
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
            "实时数据",
            "温度")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        Assert.True(viewModel.NewIoMapping!.IsStandardSource);
        viewModel.NewIoMapping.PlcAddress = "D900";
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.DirectionWrite;

        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", mapping.SignalKey);
        Assert.Equal("D900", mapping.PlcAddress);
        Assert.Equal("实时数据", mapping.BusinessGroup);
        Assert.Equal("温度", mapping.SignalName);
        Assert.Equal("Read", mapping.Direction);
        Assert.Equal(1, mapping.AddressCount);
        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task AddDataPoint_WhenSingleWriteSelected_ShouldGenerateWriteMappingAndForceCountOne()
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
            "写入数据",
            "设定值")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        viewModel.NewIoMapping!.AddressCount = 9;
        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.Equal(IoMappingOptionCatalog.CategorySingleWrite, mapping.Category);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, mapping.Direction);
        Assert.Equal(1, mapping.AddressCount);
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
            "写入数据",
            "连续设定")));

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
            "实时数据",
            "温度")));
        viewModel.StandardDataSignals.Add(new IoStandardSignalOptionVm(new ModuleIoTemplateEntry(
            "Homogenization.Debug.SingleWrite",
            "D200",
            1,
            "Int16",
            "Write",
            201,
            "单点写入",
            IoMappingOptionCatalog.CategorySingleWrite,
            "写入数据",
            "设定值")));

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
            "设备状态",
            "状态值")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        viewModel.NewIoMapping!.Category = IoMappingOptionCatalog.CategorySingleWrite;

        Assert.Null(viewModel.SelectedStandardIoSignal);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, viewModel.NewIoMapping.Direction);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.PlcAddress);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.BusinessGroup);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.SignalName);

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
            "设备状态",
            "状态值")));

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);
        viewModel.NewIoMapping!.Category = IoMappingOptionCatalog.CategoryContinuousWrite;

        Assert.Null(viewModel.SelectedStandardIoSignal);
        Assert.Equal(IoMappingOptionCatalog.DirectionWrite, viewModel.NewIoMapping.Direction);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.PlcAddress);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.BusinessGroup);
        Assert.Equal(string.Empty, viewModel.NewIoMapping.SignalName);

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
        var validationPresenter = new HardwareConfigValidationPresenter();
        var editSession = new HardwareConfigEditSession(
            validationPresenter,
            new HardwareConfigStandardSignalDraftService(),
            new HardwareConfigMappingSaveBuilder());
        return new HardwareConfigViewModel(
            new StubPermissionService { CanEditHardware = true },
            new TestLanguageService(),
            new HardwareConfigLoadSaveCoordinator(
                service ?? new StubHardwareConfigCrudService(),
                validationPresenter,
                editSession,
                new TestLanguageService()),
            new HardwareConfigDeviceSelectionCoordinator(),
            editSession);
    }

    private static NetworkDeviceVm CreatePlc()
        => new()
        {
            Id = 7,
            DeviceName = "PLC-Homogenization-01",
            DeviceType = DeviceType.PLC,
            ModuleId = "Homogenization"
        };

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
            SignalName = signalKey,
            SortOrder = 1
        };

    private sealed class StubHardwareConfigCrudService : IHardwareConfigCrudService
    {
        public IReadOnlyCollection<IoMappingVm> SavedMappings { get; private set; } = [];

        public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HardwareConfigInitResult([], []));

        public Task<IoMappingPageResult> LoadIoMappingsAsync(int networkDeviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IoMappingPageResult([], 0));

        public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
            NetworkDeviceVm? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleTemplateInfoResult(false, selectedNetworkDevice?.ModuleId, [], [], "测试标准点位。"));

        public Task<CrudOperationResult> ApplyModuleTemplateAsync(
            NetworkDeviceVm? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CrudOperationResult.Success("测试"));

        public Task<CrudOperationResult> SaveAsync(
            IReadOnlyCollection<NetworkDeviceVm> networkDevices,
            IReadOnlyCollection<SerialDeviceVm> serialDevices,
            int selectedNetworkDeviceId,
            IReadOnlyCollection<IoMappingVm> ioMappings,
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
