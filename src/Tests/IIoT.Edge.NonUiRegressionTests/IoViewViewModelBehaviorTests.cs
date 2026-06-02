using System.Globalization;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class IoViewViewModelBehaviorTests
{
    [AvaloniaFact]
    public Task LoadDevicesAsync_WhenModuleFilterSet_ShouldOnlyShowMatchingPlcs()
        => RunOnStaThreadAsync(async () =>
        {
            var devices = new[]
            {
                CreateDevice(1, "PLC-Homogenization-01", "Homogenization"),
                CreateDevice(2, "PLC-TestProcess-02", "TestProcess"),
                CreateDevice(3, "PLC-TestProcess-01", "TestProcess"),
                CreateDevice(4, "Scanner-TestProcess", "TestProcess", DeviceType.Scanner),
                CreateDevice(5, "PLC-TestProcess-Disabled", "TestProcess", isEnabled: false)
            };
            var viewModel = CreateViewModel(devices, moduleIdFilter: "TestProcess");

            await viewModel.LoadDevicesAsync();

            Assert.Equal(
                ["PLC-TestProcess-01", "PLC-TestProcess-02"],
                viewModel.Devices.Select(static x => x.DeviceName).ToArray());
        });

    [AvaloniaFact]
    public Task LoadMappingsAsync_WhenInteractionUsesSameBusinessGroup_ShouldMergeReadAndWriteIntoOneRow()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(10, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "进站触发", "D701", 1, "Int16", "Read", "信号交互", "扫码进站", "触发", 10),
                    CreateMapping(device.Id, "进站应答", "D601", 1, "Int16", "Write", "信号交互", "扫码进站", "应答", 11),
                    CreateMapping(device.Id, "搅拌速度", "D800", 1, "UInt16", "Read", "实时数据", "设备实时", "", 20)
                ]
            };
            var viewModel = CreateViewModel([device], mappings);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            var row = Assert.Single(viewModel.InteractionRows);
            Assert.Equal("扫码进站", row.BusinessGroup);
            Assert.Equal("触发", row.PlcSignalText);
            Assert.Equal("应答", row.HostSignalText);
            Assert.Equal("D701", row.PlcAddressSummary);
            Assert.Equal("D601", row.HostReplyAddressText);
            Assert.DoesNotContain(Environment.NewLine, row.PlcSignalSummary);
            Assert.DoesNotContain(Environment.NewLine, row.HostReplySummary);
            Assert.DoesNotContain("PLC", row.PlcAddressSummary);
            Assert.DoesNotContain("上位机", row.HostReplyAddressText);
            Assert.DoesNotContain("（", row.PlcAddressSummary);
            Assert.DoesNotContain("（", row.HostReplyAddressText);
            Assert.Equal(0, row.PlcSignal?.StartIndex);
            Assert.Equal(0, row.HostSignal?.StartIndex);

            var section = Assert.Single(viewModel.DataSections);
            Assert.Equal("实时数据", section.Title);
        });

    [AvaloniaFact]
    public Task LoadMappingsAsync_WhenInteractionGroupHasMultipleSignals_ShouldUseSingleLineSummary()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(11, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "触发A", "D701", 1, "Int16", "Read", "信号交互", "复合交互", "触发", 1),
                    CreateMapping(device.Id, "触发B", "D702", 1, "Int16", "Read", "信号交互", "复合交互", "触发", 2),
                    CreateMapping(device.Id, "应答A", "D601", 1, "Int16", "Write", "信号交互", "复合交互", "应答", 3),
                    CreateMapping(device.Id, "应答B", "D602", 1, "Int16", "Write", "信号交互", "复合交互", "应答", 4)
                ]
            };
            var viewModel = CreateViewModel([device], mappings);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            var row = Assert.Single(viewModel.InteractionRows);
            Assert.Equal("D701、D702", row.PlcAddressSummary);
            Assert.Equal("D601、D602", row.HostReplyAddressText);
            Assert.DoesNotContain(Environment.NewLine, row.PlcSignalSummary);
            Assert.DoesNotContain(Environment.NewLine, row.HostReplySummary);
            Assert.DoesNotContain(Environment.NewLine, row.PlcValueText);
            Assert.DoesNotContain(Environment.NewLine, row.CurrentReplyValueText);
            Assert.Contains("触发A", row.PlcSignalToolTip);
            Assert.Contains("应答A", row.HostReplyToolTip);
        });

    [AvaloniaFact]
    public Task RefreshCurrentValues_WhenSwitchingDevice_ShouldReadSelectedDeviceBufferOnly()
        => RunOnStaThreadAsync(async () =>
        {
            var deviceA = CreateDevice(21, "PLC-TestProcess-01", "TestProcess");
            var deviceB = CreateDevice(22, "PLC-TestProcess-02", "TestProcess");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [deviceA.Id] = [CreateMapping(deviceA.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "测试实时", "", 1)],
                [deviceB.Id] = [CreateMapping(deviceB.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "测试实时", "", 1)]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(deviceA.Id, readSize: 4, writeSize: 0);
            dataStore.Register(deviceB.Id, readSize: 4, writeSize: 0);
            dataStore.GetBuffer(deviceA.Id)!.UpdateReadBuffer([11]);
            dataStore.GetBuffer(deviceB.Id)!.UpdateReadBuffer([22]);
            var viewModel = CreateViewModel([deviceA, deviceB], mappings, dataStore);

            viewModel.SelectedDevice = deviceA;
            await viewModel.LoadMappingsAsync();
            Assert.Equal("11", viewModel.DataSections.Single().Signals.Single().DisplayValue);

            viewModel.SelectedDevice = deviceB;
            await viewModel.LoadMappingsAsync();
            Assert.Equal("22", viewModel.DataSections.Single().Signals.Single().DisplayValue);
        });

    [AvaloniaFact]
    public Task LoadMappingsAsync_WhenSameSignalConfiguredOnMultiplePlcs_ShouldUseSelectedPlcSavedAddress()
        => RunOnStaThreadAsync(async () =>
        {
            var deviceA = CreateDevice(25, "PLC-Homogenization-A", "Homogenization");
            var deviceB = CreateDevice(26, "PLC-Homogenization-B", "Homogenization");
            var signal = HomogenizationSignalTestProfile.Get(HomogenizationPlcSignals.Interaction.扫码进站);
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [deviceA.Id] =
                [
                    CreateMapping(
                        deviceA.Id,
                        signal.SignalKey,
                        "D901",
                        signal.AddressCount,
                        signal.DataType,
                        signal.DirectionText,
                        signal.Category,
                        signal.BusinessGroup,
                        signal.SignalName,
                        signal.SortOrder)
                ],
                [deviceB.Id] =
                [
                    CreateMapping(
                        deviceB.Id,
                        signal.SignalKey,
                        "D902",
                        signal.AddressCount,
                        signal.DataType,
                        signal.DirectionText,
                        signal.Category,
                        signal.BusinessGroup,
                        signal.SignalName,
                        signal.SortOrder)
                ]
            };
            var viewModel = CreateViewModel([deviceA, deviceB], mappings);

            viewModel.SelectedDevice = deviceA;
            await viewModel.LoadMappingsAsync();
            Assert.Equal("D901", Assert.Single(viewModel.InteractionRows).PlcAddressSummary);

            viewModel.SelectedDevice = deviceB;
            await viewModel.LoadMappingsAsync();
            Assert.Equal("D902", Assert.Single(viewModel.InteractionRows).PlcAddressSummary);
            Assert.NotEqual(signal.DefaultAddress, Assert.Single(viewModel.InteractionRows).PlcAddressSummary);
        });

    [AvaloniaFact]
    public Task RefreshCurrentValues_ShouldDecodeCommonSignalTypes()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(30, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "有符号数", "D100", 1, "Int16", "Read", "实时数据", "解码", "", 1),
                    CreateMapping(device.Id, "布尔量", "D101", 1, "Bool", "Read", "实时数据", "解码", "", 2),
                    CreateMapping(device.Id, "条码", "D102", 4, "Ascii", "Read", "条码数据", "进站条码", "", 3),
                    CreateMapping(device.Id, "浮点数组", "D106", 2, "Float", "Read", "配方数组", "配方", "", 4)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 16, writeSize: 0);
            dataStore.GetBuffer(device.Id)!.UpdateReadBuffer(
            [
                0xFFFF,
                1,
                0x4241,
                0x4443,
                0,
                0,
                0x0000,
                0x4148
            ]);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            var decodedSignals = viewModel.DataSections.SelectMany(static x => x.Signals).ToArray();
            Assert.Equal("-1", decodedSignals[0].DisplayValue);
            Assert.Equal("True", decodedSignals[1].DisplayValue);
            Assert.Equal("ABCD", decodedSignals[2].DisplayValue);

            var matrix = Assert.Single(viewModel.ArraySections);
            Assert.Equal("配方数组", matrix.Title);
            Assert.Equal("12.5", matrix.Rows.Single().Values.Single().Value);
        });

    [AvaloniaFact]
    public Task LoadMappingsAsync_WhenContinuousSignalsShareGroup_ShouldBuildMatrixSection()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(35, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "配方时间", "ZR0", 3, "UInt16", "Read", "连续读数据", "配方数组", "时间", 1),
                    CreateMapping(device.Id, "配方温度", "ZR100", 3, "Int16", "Read", "连续读数据", "配方数组", "温度", 2)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 8, writeSize: 0);
            dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([10, 20, 30, 0xFFFF, 25, 26]);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            Assert.Empty(viewModel.DataSections);
            var matrix = Assert.Single(viewModel.ArraySections);
            Assert.Equal("连续读数据", matrix.Title);
            Assert.Equal(2, matrix.Columns.Count);
            Assert.Equal(3, matrix.Rows.Count);
            Assert.Equal("10", matrix.Rows[0].Values[0].Value);
            Assert.Equal("-1", matrix.Rows[0].Values[1].Value);
            Assert.Equal("30", matrix.Rows[2].Values[0].Value);
            Assert.Equal("26", matrix.Rows[2].Values[1].Value);
        });

    [AvaloniaFact]
    public Task RefreshCurrentValues_WhenContinuousSignalsUpdate_ShouldKeepMatrixRowsStable()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(37, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "配方时间", "ZR0", 3, "UInt16", "Read", "连续读数据", "配方数组", "时间", 1),
                    CreateMapping(device.Id, "配方温度", "ZR100", 3, "UInt16", "Read", "连续读数据", "配方数组", "温度", 2)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 8, writeSize: 0);
            dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([10, 20, 30, 40, 50, 60]);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            var matrix = Assert.Single(viewModel.ArraySections);
            var firstRow = matrix.Rows[0];
            var firstCell = firstRow.Values[0];

            dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([11, 22, 33, 44, 55, 66]);
            viewModel.RefreshCurrentValues();

            Assert.Same(matrix, Assert.Single(viewModel.ArraySections));
            Assert.Same(firstRow, matrix.Rows[0]);
            Assert.Same(firstCell, matrix.Rows[0].Values[0]);
            Assert.Equal("11", firstCell.Value);
            Assert.Equal("66", matrix.Rows[2].Values[1].Value);
        });

    [AvaloniaFact]
    public Task RefreshCurrentValues_WhenWriteDataConfigured_ShouldUseWriteBufferAndDisableManualRead()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(36, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "单点写入", "D200", 1, "UInt16", "Write", "单点写数据", "写入数据", "设定值", 1),
                    CreateMapping(device.Id, "连续写入", "D220", 3, "UInt16", "Write", "连续写数据", "写入数据", "连续设定", 2)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 0, writeSize: 0);
            dataStore.GetBuffer(device.Id)!.SetWriteValue("单点写入", 0, 42);
            dataStore.GetBuffer(device.Id)!.SetWriteValue("连续写入", 0, 10);
            dataStore.GetBuffer(device.Id)!.SetWriteValue("连续写入", 1, 20);
            dataStore.GetBuffer(device.Id)!.SetWriteValue("连续写入", 2, 30);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();

            var singleWrite = Assert.Single(viewModel.DataSections);
            Assert.False(singleWrite.CanManualRead);
            Assert.Equal("42", singleWrite.Signals.Single().DisplayValue);

            var continuousWrite = Assert.Single(viewModel.ArraySections);
            Assert.False(continuousWrite.CanManualRead);
            Assert.Equal("10", continuousWrite.Rows[0].Values.Single().Value);
            Assert.Equal("30", continuousWrite.Rows[2].Values.Single().Value);
        });

    [AvaloniaFact]
    public Task WriteInteractionRow_ShouldOnlyWriteCurrentRowOutputIndex()
        => RunOnStaThreadAsync(async () =>
        {
            var device = CreateDevice(40, "PLC-Homogenization-01", "Homogenization");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [device.Id] =
                [
                    CreateMapping(device.Id, "进站触发", "D701", 1, "Int16", "Read", "信号交互", "扫码进站", "触发", 1),
                    CreateMapping(device.Id, "进站应答", "D601", 1, "Int16", "Write", "信号交互", "扫码进站", "应答", 2),
                    CreateMapping(device.Id, "PLC 出料上传", "D702", 1, "Int16", "Read", "信号交互", "出料上传", "触发", 3),
                    CreateMapping(device.Id, "上位机出料上传", "D602", 1, "Int16", "Write", "信号交互", "出料上传", "应答", 4)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 4, writeSize: 4);
            dataStore.GetBuffer(device.Id)!.SetWriteValue(0, 5);
            dataStore.GetBuffer(device.Id)!.SetWriteValue(1, 7);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();
            var inboundRow = viewModel.InteractionRows.Single(static x => x.BusinessGroup == "扫码进站");
            var outboundRow = viewModel.InteractionRows.Single(static x => x.BusinessGroup == "出料上传");
            Assert.Equal("5", inboundRow.CurrentReplyValueText);
            Assert.Equal("7", outboundRow.CurrentReplyValueText);
            Assert.Equal("5", inboundRow.HostReplyValueText);
            Assert.Equal(5, inboundRow.WriteValue);

            inboundRow.WriteValue = 9;
            viewModel.RefreshCurrentValues();
            Assert.Equal(9, inboundRow.WriteValue);

            inboundRow.WriteCommand!.Execute(null);

            var writeBuffer = dataStore.GetBuffer(device.Id)!.GetWriteBuffer();
            Assert.Equal((ushort)9, writeBuffer[0]);
            Assert.Equal((ushort)7, writeBuffer[1]);
        });

    private static TestIoViewModel CreateViewModel(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        IReadOnlyDictionary<int, List<IoMappingEntity>>? mappings = null,
        IPlcDataStore? dataStore = null,
        string? moduleIdFilter = null)
        => new(
            dataStore ?? new PlcDataStore(),
            new FakePlcConnectionManager(devices.Select(static x => x.Id).ToArray()),
            new FakeIoViewQueryFacade(devices, mappings ?? new Dictionary<int, List<IoMappingEntity>>()),
            moduleIdFilter);

    private static NetworkDeviceEntity CreateDevice(
        int id,
        string deviceName,
        string moduleId,
        DeviceType deviceType = DeviceType.PLC,
        bool isEnabled = true)
    {
        var entity = NetworkDeviceEntity.Create(deviceName, deviceType, "127.0.0.1", 102);
        entity.WithId(id);
        entity.AssignModule(moduleId, "S7");
        entity.SetEnabled(isEnabled);
        return entity;
    }

    private static IoMappingEntity CreateMapping(
        int networkDeviceId,
        string signalKey,
        string address,
        int count,
        string dataType,
        string direction,
        string category,
        string businessGroup,
        string signalName,
        int sortOrder)
    {
        var entity = IoMappingEntity.Create(networkDeviceId, signalKey, address, count, dataType, direction, category, businessGroup, signalName);
        entity.UpdateSortOrder(sortOrder);
        return entity;
    }

    private static Task RunOnStaThreadAsync(Func<Task> action) => action();

    private sealed class TestIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        IIoViewQueryFacade queryFacade,
        string? moduleIdFilter)
        : IoViewViewModel(
            dataStore,
            plcConnectionManager,
            queryFacade,
            new TestLanguageService(),
            "Test.IO",
            "Navigation_Title_IoInteract",
            "IO 交互",
            new IoViewMappingBuilder(),
            new IoViewSignalValueUpdater(),
            new IoViewBufferBindingCoordinator(dataStore),
            new IoViewInteractionWriter(dataStore),
            new IoViewManualReadService(plcConnectionManager, dataStore),
            moduleIdFilter);

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

    private sealed class FakeIoViewQueryFacade(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        IReadOnlyDictionary<int, List<IoMappingEntity>> mappings) : IIoViewQueryFacade
    {
        public Task<Result<List<NetworkDeviceEntity>>> GetNetworkDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(devices.ToList()));

        public Task<Result<IoMappingPagedDto>> GetIoMappingsAsync(
            int networkDeviceId,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var items = mappings.TryGetValue(networkDeviceId, out var deviceMappings)
                ? deviceMappings
                : [];

            return Task.FromResult(Result.Success(new IoMappingPagedDto(items, items.Count)));
        }
    }

    private sealed class FakePlcConnectionManager(IReadOnlyCollection<int> connectedIds) : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => connectedIds.Contains(networkDeviceId)
                ? new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = networkDeviceId,
                    DeviceName = networkDeviceId.ToString(CultureInfo.InvariantCulture),
                    IsConnected = true
                }
                : null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => connectedIds
                .Select(static x => new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = x,
                    DeviceName = x.ToString(CultureInfo.InvariantCulture),
                    IsConnected = true
                })
                .ToArray();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
