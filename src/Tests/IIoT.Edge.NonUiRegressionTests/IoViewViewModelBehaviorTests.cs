using System.Globalization;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class IoViewViewModelBehaviorTests
{
    [Fact]
    public Task LoadDevicesAsync_WhenModuleFilterSet_ShouldOnlyShowMatchingPlcs()
        => RunOnStaThreadAsync(async () =>
        {
            var devices = new[]
            {
                CreateDevice(1, "PLC-Homogenization-01", "Homogenization"),
                CreateDevice(2, "PLC-Stacking-02", "Stacking"),
                CreateDevice(3, "PLC-Stacking-01", "Stacking"),
                CreateDevice(4, "Scanner-Stacking", "Stacking", DeviceType.Scanner),
                CreateDevice(5, "PLC-Stacking-Disabled", "Stacking", isEnabled: false)
            };
            var viewModel = CreateViewModel(devices, moduleIdFilter: "Stacking");

            await viewModel.LoadDevicesAsync();

            Assert.Equal(
                ["PLC-Stacking-01", "PLC-Stacking-02"],
                viewModel.Devices.Select(static x => x.DeviceName).ToArray());
        });

    [Fact]
    public Task LoadMappingsAsync_WhenInteractionUsesSameGroupName_ShouldMergeReadAndWriteIntoOneRow()
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
            Assert.Equal("扫码进站", row.GroupName);
            Assert.Equal("进站触发（触发）", row.PlcSignalText);
            Assert.Equal("进站应答（应答）", row.HostSignalText);
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
            Assert.Equal("实时数据 - 设备实时", section.Title);
        });

    [Fact]
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
            Assert.Equal("D701，D702", row.PlcAddressSummary);
            Assert.Equal("D601，D602", row.HostReplyAddressText);
            Assert.DoesNotContain(Environment.NewLine, row.PlcSignalSummary);
            Assert.DoesNotContain(Environment.NewLine, row.HostReplySummary);
            Assert.DoesNotContain(Environment.NewLine, row.PlcValueText);
            Assert.DoesNotContain(Environment.NewLine, row.CurrentReplyValueText);
            Assert.Contains("触发A", row.PlcSignalToolTip);
            Assert.Contains("应答A", row.HostReplyToolTip);
        });

    [Fact]
    public Task RefreshCurrentValues_WhenSwitchingDevice_ShouldReadSelectedDeviceBufferOnly()
        => RunOnStaThreadAsync(async () =>
        {
            var deviceA = CreateDevice(21, "PLC-Stacking-01", "Stacking");
            var deviceB = CreateDevice(22, "PLC-Stacking-02", "Stacking");
            var mappings = new Dictionary<int, List<IoMappingEntity>>
            {
                [deviceA.Id] = [CreateMapping(deviceA.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "叠片实时", "", 1)],
                [deviceB.Id] = [CreateMapping(deviceB.Id, "层数", "D100", 1, "UInt16", "Read", "实时数据", "叠片实时", "", 1)]
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

    [Fact]
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
            Assert.Equal("配方数组 - 配方", matrix.Title);
            Assert.Equal("12.5", matrix.Rows.Single().Values.Single().Value);
        });

    [Fact]
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
            Assert.Equal("配方数组", matrix.Title);
            Assert.Equal(2, matrix.Columns.Count);
            Assert.Equal(3, matrix.Rows.Count);
            Assert.Equal("10", matrix.Rows[0].Values[0].Value);
            Assert.Equal("-1", matrix.Rows[0].Values[1].Value);
            Assert.Equal("30", matrix.Rows[2].Values[0].Value);
            Assert.Equal("26", matrix.Rows[2].Values[1].Value);
        });

    [Fact]
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
                    CreateMapping(device.Id, "出料触发", "D702", 1, "Int16", "Read", "信号交互", "出料上传", "触发", 3),
                    CreateMapping(device.Id, "出料应答", "D602", 1, "Int16", "Write", "信号交互", "出料上传", "应答", 4)
                ]
            };
            var dataStore = new PlcDataStore();
            dataStore.Register(device.Id, readSize: 4, writeSize: 4);
            dataStore.GetBuffer(device.Id)!.SetWriteValue(0, 5);
            dataStore.GetBuffer(device.Id)!.SetWriteValue(1, 7);
            var viewModel = CreateViewModel([device], mappings, dataStore);

            viewModel.SelectedDevice = device;
            await viewModel.LoadMappingsAsync();
            var inboundRow = viewModel.InteractionRows.Single(static x => x.GroupName == "扫码进站");
            var outboundRow = viewModel.InteractionRows.Single(static x => x.GroupName == "出料上传");
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
            new FakeIoSender(devices, mappings ?? new Dictionary<int, List<IoMappingEntity>>()),
            moduleIdFilter);

    private static NetworkDeviceEntity CreateDevice(
        int id,
        string deviceName,
        string moduleId,
        DeviceType deviceType = DeviceType.PLC,
        bool isEnabled = true)
        => new(deviceName, deviceType, "127.0.0.1", 102)
        {
            Id = id,
            ModuleId = moduleId,
            IsEnabled = isEnabled
        };

    private static IoMappingEntity CreateMapping(
        int networkDeviceId,
        string label,
        string address,
        int count,
        string dataType,
        string direction,
        string category,
        string groupName,
        string displayRole,
        int sortOrder)
        => new(networkDeviceId, label, address, count, dataType, direction, category, groupName, displayRole)
        {
            SortOrder = sortOrder
        };

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

    private sealed class TestIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender,
        string? moduleIdFilter)
        : IoViewViewModel(dataStore, plcConnectionManager, sender, "Test.IO", "IO 交互", moduleIdFilter);

    private sealed class FakeIoSender(
        IReadOnlyCollection<NetworkDeviceEntity> devices,
        IReadOnlyDictionary<int, List<IoMappingEntity>> mappings) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetAllNetworkDevicesQuery)
            {
                return Task.FromResult((TResponse)(object)Result.Success(devices.ToList()));
            }

            if (request is GetIoMappingsByDeviceQuery query)
            {
                var items = mappings.TryGetValue(query.NetworkDeviceId, out var deviceMappings)
                    ? deviceMappings
                    : [];
                return Task.FromResult((TResponse)(object)Result.Success(new IoMappingPagedDto(items, items.Count)));
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
