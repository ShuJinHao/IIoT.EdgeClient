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
    [Fact]
    public Task AddInteraction_WhenStandardGroupSelected_ShouldAddReadAndWriteTogether()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        viewModel.StandardInteractionGroups.Add(new IoStandardSignalGroupOptionVm(
            "扫码进站",
            [
                new ModuleIoTemplateEntry(
                    "Homogenization.InboundTrigger",
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
                    "Homogenization.InboundAck",
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
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.InboundTrigger" && x.Direction == "Read");
        Assert.Contains(viewModel.IoMappings, x => x.SignalKey == "Homogenization.InboundAck" && x.Direction == "Write");
        Assert.All(viewModel.IoMappings, x => Assert.Equal("扫码进站", x.BusinessGroup));
        return Task.CompletedTask;
    });

    [Fact]
    public Task AddDataPoint_WhenCustomPointSelected_ShouldGenerateSingleReadMapping()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();

        viewModel.OpenAddDataPointMappingDialogCommand.Execute(null);

        Assert.NotNull(viewModel.NewIoMapping);
        Assert.True(viewModel.NewIoMapping!.IsCustomSource);
        viewModel.NewIoMapping.PlcAddress = "D900";
        viewModel.NewIoMapping.SignalName = "临时调试值";
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.DirectionWrite;

        viewModel.ConfirmAddIoMappingCommand.Execute(null);

        var mapping = Assert.Single(viewModel.IoMappings);
        Assert.StartsWith("Manual.", mapping.SignalKey);
        Assert.Equal("D900", mapping.PlcAddress);
        Assert.Equal("自定义单点数据", mapping.BusinessGroup);
        Assert.Equal("临时调试值", mapping.SignalName);
        Assert.Equal("Read", mapping.Direction);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeleteIoPoint_WhenInteractionSelected_ShouldDeleteWholeBusinessGroup()
        => RunOnStaThreadAsync(() =>
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedNetworkDevice = CreatePlc();
        var read = CreateMapping("Homogenization.RecipeTrigger", "D703", "Read", "工艺参数上传");
        var write = CreateMapping("Homogenization.RecipeAck", "D603", "Write", "工艺参数上传");
        var data = CreateMapping("Homogenization.RealtimeTemperature", "D301", "Read", "实时数据", IoMappingOptionCatalog.CategorySingleRead);
        viewModel.IoMappings.Add(read);
        viewModel.IoMappings.Add(write);
        viewModel.IoMappings.Add(data);

        viewModel.DeleteIoMappingCommand.Execute(read);

        var remain = Assert.Single(viewModel.IoMappings);
        Assert.Equal("Homogenization.RealtimeTemperature", remain.SignalKey);
        return Task.CompletedTask;
    });

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static HardwareConfigViewModel CreateViewModel()
        => new(
            new StubHardwareConfigCrudService(),
            new StubPermissionService { CanEditHardware = true },
            new TestLanguageService());

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
        public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HardwareConfigInitResult([], []));

        public Task<IoMappingPageResult> LoadIoMappingsAsync(int networkDeviceId, CancellationToken cancellationToken = default)
            => Task.FromResult(new IoMappingPageResult([], 0));

        public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
            NetworkDeviceVm? selectedNetworkDevice,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModuleTemplateInfoResult(false, selectedNetworkDevice?.ModuleId, [], "测试标准点位。"));

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
            => Task.FromResult(CrudOperationResult.Success("测试"));
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
