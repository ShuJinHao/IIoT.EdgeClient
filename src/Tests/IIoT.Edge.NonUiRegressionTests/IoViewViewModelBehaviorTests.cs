using System.Globalization;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Features.Hardware.IOView;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class IoViewViewModelBehaviorTests
{
    [Fact]
    public void IoSignalModel_ShouldDisplayTheStoredRemarkWithoutPageSpecificRewriting()
    {
        var model = new IoSignalModel { Remark = "匀浆模块 - 配方时间" };

        Assert.Equal("匀浆模块 - 配方时间", model.Remark);
        Assert.Equal("匀浆模块 - 配方时间", model.MatrixColumnTitle);
    }

    [AvaloniaFact]
    public async Task LoadDevicesAsync_WhenDevicesLoaded_ShouldShowConfiguredPlcs()
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
            ["PLC-Homogenization-01", "PLC-TestProcess-01", "PLC-TestProcess-02", "PLC-TestProcess-Disabled"],
            viewModel.Devices.Select(static x => x.DeviceName).ToArray());
    }

    [AvaloniaFact]
    public async Task LoadDevicesAsync_WhenSharedSelectionIsAll_ShouldNotAutoSelectFirstPlc()
    {
        var devices = new[]
        {
            CreateDevice(6, "PLC-A", "TestProcess"),
            CreateDevice(7, "PLC-B", "TestProcess")
        };
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel(devices, deviceSelectionService: selectionService);

        await viewModel.OnActivatedAsync();

        Assert.Null(viewModel.SelectedDevice);
        Assert.False(viewModel.HasSelectedDevice);
        Assert.False(viewModel.ManualReadCommand.CanExecute(null));
        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
    }

    [AvaloniaFact]
    public async Task LoadDevicesAsync_WhenSharedSelectionMatchesDeviceName_ShouldSelectThatPlc()
    {
        var deviceA = CreateDevice(8, "PLC-A", "TestProcess");
        var deviceB = CreateDevice(9, "PLC-B", "TestProcess");
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice(deviceB.DeviceName);
        var viewModel = CreateViewModel([deviceA, deviceB], deviceSelectionService: selectionService);

        await viewModel.LoadDevicesAsync();

        Assert.Equal(deviceB.DeviceName, viewModel.SelectedDevice?.DeviceName);
        Assert.Equal(deviceB.DeviceName, selectionService.SelectedDeviceKey);
    }

    [AvaloniaFact]
    public async Task LoadDevicesAsync_WhenSharedSelectionMatchesDisabledPlc_ShouldKeepDeviceVisibleWithoutChangingGlobalSelection()
    {
        var enabled = CreateDevice(12, "PLC-Enabled", "TestProcess");
        var disabled = CreateDevice(13, "PLC-Disabled", "TestProcess", isEnabled: false);
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice(disabled.DeviceName);
        var viewModel = CreateViewModel([enabled, disabled], deviceSelectionService: selectionService);

        await viewModel.LoadDevicesAsync();

        Assert.Equal(disabled.DeviceName, viewModel.SelectedDevice?.DeviceName);
        Assert.Contains(viewModel.Devices, device => device.DeviceName == disabled.DeviceName);
        Assert.Equal(disabled.DeviceName, selectionService.SelectedDeviceKey);
    }

    [AvaloniaFact]
    public async Task SelectedDevice_WhenSetInsideIoPage_ShouldNotWriteSharedSelection()
    {
        var deviceA = CreateDevice(14, "PLC-A", "TestProcess");
        var deviceB = CreateDevice(15, "PLC-B", "TestProcess");
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel([deviceA, deviceB], deviceSelectionService: selectionService);
        await viewModel.LoadDevicesAsync();

        viewModel.SelectedDevice = deviceB;

        Assert.Equal(IDeviceSelectionService.AllFilterKey, selectionService.SelectedDeviceKey);
        Assert.Equal(deviceB.DeviceName, viewModel.SelectedDevice?.DeviceName);
    }

    [AvaloniaFact]
    public async Task SharedSelectionChanged_WhenDeviceListAlreadyLoaded_ShouldNotReloadDevices()
    {
        var deviceA = CreateDevice(16, "PLC-A", "TestProcess");
        var deviceB = CreateDevice(17, "PLC-B", "TestProcess");
        var selectionService = new DeviceSelectionService();
        var facade = new FakeIoViewQueryFacade(
            [deviceA, deviceB],
            new Dictionary<int, List<IoMappingEntity>>());
        var dataStore = new PlcDataStore();
        var viewModel = new TestIoViewModel(
            dataStore,
            new FakePlcConnectionManager([deviceA.Id, deviceB.Id]),
            facade,
            null,
            selectionService);
        await viewModel.OnActivatedAsync();

        selectionService.SelectDevice(deviceB.DeviceName);

        Assert.Equal(1, facade.NetworkDeviceQueryCount);
        Assert.Equal(deviceB.DeviceName, viewModel.SelectedDevice?.DeviceName);
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenInteractionUsesSameBusinessGroup_ShouldMergeReadAndWriteIntoOneRow()
    {
        var device = CreateDevice(10, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "Homogenization.Interaction.Inbound", "D701", 1, "Int16", "Read", "信号交互", "扫码进站", 10, "进站触发"),
                CreateMapping(device.Id, "Homogenization.Interaction.InboundReply", "D601", 1, "Int16", "Write", "信号交互", "扫码进站", 11, "进站应答"),
                CreateMapping(device.Id, "搅拌速度", "D800", 1, "UInt16", "Read", "实时数据", "设备实时", 20)
            ]
        };
        var viewModel = CreateViewModel([device], mappings);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        var row = Assert.Single(viewModel.InteractionRows);
        Assert.Equal("扫码进站", row.BusinessGroup);
        Assert.Equal("进站触发", row.PlcSignalText);
        Assert.Equal("进站应答", row.HostSignalText);
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
        Assert.Equal(IoMappingOptionCatalog.CategorySingleRead, section.Title);
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenMappingsUseFiveCategories_ShouldExposeSameFiveIoBuckets()
    {
        var device = CreateDevice(18, "PLC-TestProcess-01", "TestProcess");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "Signal.Interaction.Read", "D100", 1, "UInt16", "Read", IoMappingOptionCatalog.CategoryInteraction, "交互", 1),
                CreateMapping(device.Id, "Signal.SingleRead", "D200", 1, "UInt16", "Read", IoMappingOptionCatalog.CategorySingleRead, "单点读", 2),
                CreateMapping(device.Id, "Signal.ContinuousRead", "D300", 8, "Ascii", "Read", IoMappingOptionCatalog.CategoryContinuousRead, "连续读", 3),
                CreateMapping(device.Id, "Signal.SingleWrite", "D400", 1, "UInt16", "Write", IoMappingOptionCatalog.CategorySingleWrite, "单点写", 4),
                CreateMapping(device.Id, "Signal.ContinuousWrite", "D500", 4, "UInt16", "Write", IoMappingOptionCatalog.CategoryContinuousWrite, "连续写", 5)
            ]
        };
        var viewModel = CreateViewModel([device], mappings);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        Assert.Single(viewModel.InteractionRows);
        Assert.Equal(IoMappingOptionCatalog.CategorySingleRead, Assert.Single(viewModel.SingleReadSections).Title);
        Assert.Equal(IoMappingOptionCatalog.CategoryContinuousRead, Assert.Single(viewModel.ContinuousReadSections).Title);
        Assert.Equal(IoMappingOptionCatalog.CategorySingleWrite, Assert.Single(viewModel.SingleWriteSections).Title);
        Assert.Equal(IoMappingOptionCatalog.CategoryContinuousWrite, Assert.Single(viewModel.ContinuousWriteSections).Title);
        Assert.Equal(4, viewModel.DataSections.Count);
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenContinuousReadIsAscii_ShouldStayInContinuousReadBucket()
    {
        var device = CreateDevice(19, "PLC-TestProcess-01", "TestProcess");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "Signal.Barcode", "R9660", 8, "Ascii", "Read", IoMappingOptionCatalog.CategoryContinuousRead, "测试插件甲只读采集模块", 1)
            ]
        };
        var viewModel = CreateViewModel([device], mappings);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        Assert.Empty(viewModel.SingleReadSections);
        var section = Assert.Single(viewModel.ContinuousReadSections);
        var signal = Assert.Single(section.Signals);
        Assert.Equal("R9660", signal.PlcAddress);
        Assert.Equal(8, signal.AddressCount);
        Assert.Equal("Ascii", signal.DataType);
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenInteractionGroupHasMultipleSignals_ShouldUseSingleLineSummary()
    {
        var device = CreateDevice(11, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "Signal.TriggerA", "D701", 1, "Int16", "Read", "信号交互", "复合交互", 1, "触发A"),
                CreateMapping(device.Id, "Signal.TriggerB", "D702", 1, "Int16", "Read", "信号交互", "复合交互", 2, "触发B"),
                CreateMapping(device.Id, "Signal.ReplyA", "D601", 1, "Int16", "Write", "信号交互", "复合交互", 3, "应答A"),
                CreateMapping(device.Id, "Signal.ReplyB", "D602", 1, "Int16", "Write", "信号交互", "复合交互", 4, "应答B")
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
    }

    [AvaloniaFact]
    public async Task RefreshCurrentValues_WhenSwitchingDevice_ShouldReadSelectedDeviceBufferOnly()
    {
        var deviceA = CreateDevice(21, "PLC-TestProcess-01", "TestProcess");
        var deviceB = CreateDevice(22, "PLC-TestProcess-02", "TestProcess");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [deviceA.Id] = [CreateMapping(deviceA.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "测试实时", 1)],
            [deviceB.Id] = [CreateMapping(deviceB.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "测试实时", 1)]
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
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenSameSignalConfiguredOnMultiplePlcs_ShouldUseSelectedPlcSavedAddress()
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
    }

    [AvaloniaFact]
    public async Task RefreshCurrentValues_ShouldDecodeCommonSignalTypes()
    {
        var device = CreateDevice(30, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "有符号数", "D100", 1, "Int16", "Read", "实时数据", "解码", 1),
                CreateMapping(device.Id, "布尔量", "D101", 1, "Bool", "Read", "实时数据", "解码", 2),
                CreateMapping(device.Id, "条码", "D102", 4, "Ascii", "Read", "条码数据", "进站条码", 3),
                CreateMapping(device.Id, "浮点数组", "D106", 2, "Float", "Read", "配方数组", "配方", 4),
                CreateMapping(device.Id, "实际产量", "D108", 2, "Int32", "Read", "实时数据", "解码", 5),
                CreateMapping(device.Id, "无符号累计", "D110", 2, "UInt32", "Read", "实时数据", "解码", 6),
                CreateMapping(device.Id, "双字状态", "D112", 2, "DWord", "Read", "实时数据", "解码", 7)
            ]
        };
        var dataStore = new PlcDataStore();
        dataStore.Register(device.Id, readSize: 20, writeSize: 0);
        dataStore.GetBuffer(device.Id)!.UpdateReadBuffer(
        [
            0xFFFF,
            1,
            0x4241,
            0x4443,
            0,
            0,
            0x0000,
            0x4148,
            54621,
            15,
            0xFFFF,
            0x0001,
            0x0001,
            0x0001
        ]);
        var viewModel = CreateViewModel([device], mappings, dataStore);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        var decodedSignals = viewModel.DataSections.SelectMany(static x => x.Signals).ToArray();
        Assert.Equal("-1", decodedSignals[0].DisplayValue);
        Assert.Equal("True", decodedSignals[1].DisplayValue);
        Assert.Equal("ABCD", decodedSignals[2].DisplayValue);

        var floatSignal = decodedSignals.Single(static signal => signal.SignalKey == "浮点数组");
        Assert.Equal("12.5", floatSignal.DisplayValue);

        var int32Signal = decodedSignals.Single(static signal => signal.SignalKey == "实际产量");
        Assert.Equal("1037661", int32Signal.DisplayValue);
        Assert.Equal(1037661, int32Signal.Value);

        var uint32Signal = decodedSignals.Single(static signal => signal.SignalKey == "无符号累计");
        Assert.Equal("131071", uint32Signal.DisplayValue);
        Assert.Equal(131071, uint32Signal.Value);

        var dwordSignal = decodedSignals.Single(static signal => signal.SignalKey == "双字状态");
        Assert.Equal("65537", dwordSignal.DisplayValue);
        Assert.Equal(65537, dwordSignal.Value);
    }

    [AvaloniaFact]
    public async Task LoadMappingsAsync_WhenContinuousSignalsShareGroup_ShouldBuildContinuousReadSection()
    {
        var device = CreateDevice(35, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "Homogenization.Recipe.Time", "ZR0", 3, "UInt16", "Read", "连续读数据", "配方数组", 1, "配方时间"),
                CreateMapping(device.Id, "Homogenization.Recipe.Temperature", "ZR100", 3, "Int16", "Read", "连续读数据", "配方数组", 2, "配方温度")
            ]
        };
        var dataStore = new PlcDataStore();
        dataStore.Register(device.Id, readSize: 8, writeSize: 0);
        dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([10, 20, 30, 0xFFFF, 25, 26]);
        var viewModel = CreateViewModel([device], mappings, dataStore);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        Assert.Empty(viewModel.SingleReadSections);
        var section = Assert.Single(viewModel.ContinuousReadSections);
        Assert.Same(section, Assert.Single(viewModel.DataSections));
        Assert.Equal("连续读数据", section.Title);
        Assert.Equal(2, section.Signals.Count);
        Assert.Equal("配方时间", section.Signals[0].MatrixColumnTitle);
        Assert.Equal("配方温度", section.Signals[1].MatrixColumnTitle);
        Assert.DoesNotContain("Homogenization.", string.Join(",", section.Signals.Select(static x => x.MatrixColumnTitle)));
        Assert.Equal("10, 20, 30", section.Signals[0].DisplayValue);
        Assert.Equal("-1, 25, 26", section.Signals[1].DisplayValue);
    }

    [AvaloniaFact]
    public async Task RefreshCurrentValues_WhenContinuousSignalsUpdate_ShouldKeepContinuousReadSignalsStable()
    {
        var device = CreateDevice(37, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "配方时间", "ZR0", 3, "UInt16", "Read", "连续读数据", "配方数组", 1),
                CreateMapping(device.Id, "配方温度", "ZR100", 3, "UInt16", "Read", "连续读数据", "配方数组", 2)
            ]
        };
        var dataStore = new PlcDataStore();
        dataStore.Register(device.Id, readSize: 8, writeSize: 0);
        dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([10, 20, 30, 40, 50, 60]);
        var viewModel = CreateViewModel([device], mappings, dataStore);

        viewModel.SelectedDevice = device;
        await viewModel.LoadMappingsAsync();

        var section = Assert.Single(viewModel.ContinuousReadSections);
        var firstSignal = section.Signals[0];

        dataStore.GetBuffer(device.Id)!.UpdateReadBuffer([11, 22, 33, 44, 55, 66]);
        viewModel.RefreshCurrentValues();

        Assert.Same(section, Assert.Single(viewModel.ContinuousReadSections));
        Assert.Same(firstSignal, section.Signals[0]);
        Assert.Equal("11, 22, 33", firstSignal.DisplayValue);
        Assert.Equal("66", section.Signals[1].ExpandedValues[2].Value);
    }

    [AvaloniaFact]
    public async Task RefreshCurrentValues_WhenWriteDataConfigured_ShouldUseWriteBufferAndDisableManualRead()
    {
        var device = CreateDevice(36, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "单点写入", "D200", 1, "UInt16", "Write", "单点写数据", "写入数据", 1),
                CreateMapping(device.Id, "连续写入", "D220", 3, "UInt16", "Write", "连续写数据", "写入数据", 2)
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

        var singleWrite = Assert.Single(viewModel.SingleWriteSections);
        Assert.False(singleWrite.CanManualRead);
        Assert.Equal("42", singleWrite.Signals.Single().DisplayValue);

        var continuousWrite = Assert.Single(viewModel.ContinuousWriteSections);
        Assert.False(continuousWrite.CanManualRead);
        Assert.Equal("10, 20, 30", continuousWrite.Signals.Single().DisplayValue);
    }

    [AvaloniaFact]
    public async Task ManualReadAsync_WhenPlcDisconnected_ShouldReturnClearErrorWithoutReading()
    {
        var device = CreateDevice(39, "PLC-TestProcess-01", "TestProcess");
        var dataStore = new PlcDataStore();
        dataStore.Register(device.Id, readSize: 4, writeSize: 0);
        var service = new IoViewManualReadService(
            new FakePlcConnectionManager([]),
            dataStore);

        var result = await service.ReadAsync(
            device.Id,
            [],
            []);

        Assert.False(result.ShouldRefreshValues);
        Assert.Equal("PLC 未连接，无法读取。", result.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task WriteInteractionRow_ShouldOnlyWriteCurrentRowOutputIndex()
    {
        var device = CreateDevice(40, "PLC-Homogenization-01", "Homogenization");
        var mappings = new Dictionary<int, List<IoMappingEntity>>
        {
            [device.Id] =
            [
                CreateMapping(device.Id, "进站触发", "D701", 1, "Int16", "Read", "信号交互", "扫码进站", 1),
                CreateMapping(device.Id, "进站应答", "D601", 1, "Int16", "Write", "信号交互", "扫码进站", 2),
                CreateMapping(device.Id, "PLC 出料上传", "D702", 1, "Int16", "Read", "信号交互", "出料上传", 3),
                CreateMapping(device.Id, "上位机出料上传", "D602", 1, "Int16", "Write", "信号交互", "出料上传", 4)
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
    }

    private static TestIoViewModel CreateViewModel(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        IReadOnlyDictionary<int, List<IoMappingEntity>>? mappings = null,
        IPlcDataStore? dataStore = null,
        string? moduleIdFilter = null,
        IDeviceSelectionService? deviceSelectionService = null)
        => new(
            dataStore ?? new PlcDataStore(),
            new FakePlcConnectionManager(devices.Select(static x => x.Id).ToArray()),
            new FakeIoViewQueryFacade(devices, mappings ?? new Dictionary<int, List<IoMappingEntity>>()),
            moduleIdFilter,
            deviceSelectionService ?? new DeviceSelectionService());

    private static NetworkDeviceEntity CreateDevice(
        int id,
        string deviceName,
        string moduleId,
        DeviceType deviceType = DeviceType.PLC,
        bool isEnabled = true)
    {
        var entity = NetworkDeviceEntity.Create(deviceName, deviceType, "127.0.0.1", 102);
        entity.WithId(id);
        entity.UpdateDeviceModel("S7");
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
        int sortOrder,
        string? remark = null)
    {
        var entity = IoMappingEntity.Create(networkDeviceId, signalKey, address, count, dataType, direction, category, businessGroup);
        entity.UpdateMetadata(signalKey, dataType, direction, category, businessGroup, remark);
        entity.UpdateSortOrder(sortOrder);
        return entity;
    }


    private sealed class TestIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        IIoViewQueryFacade queryFacade,
        string? moduleIdFilter,
        IDeviceSelectionService deviceSelectionService)
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
            deviceSelectionService,
            moduleIdFilter)
    {
        protected override void RunOnUiThread(Action action) => action();
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

    private sealed class FakeIoViewQueryFacade(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        IReadOnlyDictionary<int, List<IoMappingEntity>> mappings) : IIoViewQueryFacade
    {
        public int NetworkDeviceQueryCount { get; private set; }

        public Task<Result<List<NetworkDeviceEntity>>> GetNetworkDevicesAsync(CancellationToken cancellationToken = default)
        {
            NetworkDeviceQueryCount++;
            return Task.FromResult(Result.Success(devices.ToList()));
        }

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
