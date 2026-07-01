using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.SharedKernel.Specification;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HardwareConfigFullSyncBehaviorTests
{
    [Theory]
    [InlineData("", "192.168.0.10", 102)]
    [InlineData("PLC-A", "", 102)]
    [InlineData("PLC-A", "192.168.0.10", 0)]
    public void NetworkDeviceEntity_WhenRequiredFieldsInvalid_ShouldReject(
        string deviceName,
        string ipAddress,
        int port)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, ipAddress, port));
    }

    [Theory]
    [InlineData("", 9600, 8, "One", "None")]
    [InlineData("COM1", 0, 8, "One", "None")]
    [InlineData("COM1", 9600, 0, "One", "None")]
    [InlineData("COM1", 9600, 8, "", "None")]
    [InlineData("COM1", 9600, 8, "One", "")]
    public void SerialDeviceEntity_WhenPortFieldsInvalid_ShouldReject(
        string portName,
        int baudRate,
        int dataBits,
        string stopBits,
        string parity)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SerialDeviceEntity.Create("Scanner-A", "Scanner", portName, baudRate)
                .UpdatePort(portName, baudRate, dataBits, stopBits, parity));
    }

    [Theory]
    [InlineData(0, "Signal.A", "D0", 1, "Int16", "Read")]
    [InlineData(1, "", "D0", 1, "Int16", "Read")]
    [InlineData(1, "Signal.A", "D0", 0, "Int16", "Read")]
    [InlineData(1, "Signal.A", "D0", 1, "", "Read")]
    [InlineData(1, "Signal.A", "D0", 1, "Int16", "")]
    public void IoMappingEntity_WhenRequiredFieldsInvalid_ShouldReject(
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            IoMappingEntity.Create(networkDeviceId, signalKey, plcAddress, addressCount, dataType, direction));
    }

    [Fact]
    public void IoMappingEntity_WhenPlcAddressEmpty_ShouldKeepUnconfiguredState()
    {
        var entity = IoMappingEntity.Create(1, "Signal.A", "", 1, "Int16", "Read");

        Assert.Equal(string.Empty, entity.PlcAddress);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenPlcModuleMissing_ShouldReturnDomainFailure()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-A",
                        DeviceType.PLC,
                        "S7",
                        string.Empty,
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("网络设备地址不能为空。", result.ErrorMessage);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenSubmissionInvalid_ShouldNotDeleteExistingRows()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"),
            CreateNetworkDevice(id: 2, name: "PLC-B"));
        var handler = new SaveNetworkDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        1,
                        "PLC-A",
                        DeviceType.PLC,
                        "S7",
                        string.Empty,
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal([1, 2], repo.Items.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenDeviceMissingFromSubmission_ShouldDeleteIt()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"),
            CreateNetworkDevice(id: 2, name: "PLC-B"));
        var handler = new SaveNetworkDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        1,
                        "PLC-A-UPDATED",
                        DeviceType.PLC,
                        "S7",
                        "192.168.0.11",
                        102,
                        null,
                        null,
                        null,
                        5000,
                        true,
                        "updated")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            repo.Items.OrderBy(x => x.Id),
            device =>
            {
                Assert.Equal(1, device.Id);
                Assert.Equal("PLC-A-UPDATED", device.DeviceName);
                Assert.Equal("192.168.0.11", device.IpAddress);
                Assert.Equal(5000, device.ConnectTimeout);
                Assert.Equal("updated", device.Remark);
            });
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenMcProtocolFrameSubmitted_ShouldPersistNormalizedFrame()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-MC",
                        DeviceType.PLC,
                        "Mc",
                        "192.168.0.10",
                        65530,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null,
                        "e4")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var device = Assert.Single(repo.Items);
        Assert.Equal("E4", device.ProtocolFrame);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenMcProtocolFrameInvalid_ShouldRejectSubmission()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-MC",
                        DeviceType.PLC,
                        "Mc",
                        "192.168.0.10",
                        65530,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null,
                        "E5")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("MC PLC 协议帧只支持 E3 或 E4。", result.ErrorMessage);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveSerialDevicesHandler_WhenDeviceMissingFromSubmission_ShouldDeleteItAndPreserveExtendedFields()
    {
        var repo = new InMemoryRepository<SerialDeviceEntity>(
            CreateSerialDevice(id: 1, name: "Scanner-A"),
            CreateSerialDevice(id: 2, name: "Scanner-B"));
        var handler = new SaveSerialDevicesHandler(repo);

        var result = await handler.Handle(
            new SaveSerialDevicesCommand(
                [
                    new SerialDeviceDto(
                        1,
                        "Scanner-A",
                        "Scanner",
                        "COM3",
                        115200,
                        7,
                        "Two",
                        "Odd",
                        "A1",
                        "A2",
                        false,
                        "preserved")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            repo.Items,
            device =>
            {
                Assert.Equal(1, device.Id);
                Assert.Equal(7, device.DataBits);
                Assert.Equal("Two", device.StopBits);
                Assert.Equal("Odd", device.Parity);
                Assert.Equal("A1", device.SendCmd1);
                Assert.Equal("A2", device.SendCmd2);
                Assert.False(device.IsEnabled);
                Assert.Equal("preserved", device.Remark);
            });
    }

    [Fact]
    public async Task SaveIoMappingsHandler_WhenMappingMissingFromSubmission_ShouldDeleteItAndPreserveRemark()
    {
        var repo = new InMemoryRepository<IoMappingEntity>(
            CreateIoMapping(id: 1, deviceId: 9, signalKey: "Signal.A", remark: "keep"),
            CreateIoMapping(id: 2, deviceId: 9, signalKey: "Signal.B", remark: "delete"),
            CreateIoMapping(id: 3, deviceId: 10, signalKey: "Signal.C", remark: "other-device"));
        var handler = new SaveIoMappingsHandler(repo);

        var result = await handler.Handle(
            new SaveIoMappingsCommand(
                9,
                [
                    new IoMappingDto(
                        1,
                        9,
                        "Signal.A",
                        "DB1.DBW0",
                        2,
                        "Int16",
                        "Read",
                        "单点读数据",
                        string.Empty,
                        1,
                        "updated-remark")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Items.Count);
        Assert.DoesNotContain(repo.Items, x => x.Id == 2);
        Assert.Contains(repo.Items, x => x.Id == 1 && x.Remark == "updated-remark");
        Assert.Contains(repo.Items, x => x.Id == 3 && x.Remark == "other-device");
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenExistingPlcIsRemoved_ShouldCallStopDeviceAsync()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                2,
                []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([1], plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenSelectedPlcMappingsChange_ShouldReloadThatPlcOnly()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A", plcAddress: "DB1.DBW0", remark: "old")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A"), CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    CreateIoMappingDto(id: 11, deviceId: 1, signalKey: "Signal.A", remark: "new")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Equal(["PLC-A"], plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenPlcUnchanged_ShouldNotReloadIt()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A", plcAddress: "DB1.DBW0", remark: "same")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A"), CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    CreateIoMappingDto(id: 11, deviceId: 1, signalKey: "Signal.A", remark: "same")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenTwoPlcsShareTheSameName_ShouldReloadBothChangedTargets()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-DUP", ipAddress: "192.168.0.10", port1: 102),
                CreateNetworkDevice(id: 2, name: "PLC-DUP", ipAddress: "192.168.0.11", port1: 102)
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 1, name: "PLC-DUP", ipAddress: "192.168.0.10", port1: 103),
                    CreateNetworkDto(id: 2, name: "PLC-DUP", ipAddress: "192.168.0.11", port1: 104)
                ],
                [],
                1,
                []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, plcManager.ReloadedDeviceNames.Count);
        Assert.All(plcManager.ReloadedDeviceNames, x => Assert.Equal("PLC-DUP", x));
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenPersistenceSucceedsButStopOrReloadFails_ShouldReturnSavedButNotAppliedMessage()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102)
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        plcManager.StopFailures[1] = new InvalidOperationException("stop boom");
        plcManager.ReloadFailures["PLC-B"] = new InvalidOperationException("reload boom");
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 2, name: "PLC-B", port1: 103)
                ],
                [],
                2,
                []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("配置已保存，但", result.Message);
        Assert.Contains("以下 PLC 已删除停机失败：", result.Message);
        Assert.Contains("以下 PLC 重载失败：", result.Message);
        Assert.Contains("PLC-A", result.Message);
        Assert.Contains("PLC-B", result.Message);
        Assert.Equal([1], plcManager.StoppedDeviceIds);
        Assert.Equal(["PLC-B"], plcManager.ReloadedDeviceNames);
    }

    private static SaveHardwareConfigHandler CreateSaveHandler(HardwareConfigSender sender, FakePlcConnectionManager plcManager)
        => new(
            sender,
            new StubPermissionService { CanEditHardware = true },
            plcManager);

    private static NetworkDeviceEntity CreateNetworkDevice(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
    {
        var entity = NetworkDeviceEntity.Create(name, DeviceType.PLC, ipAddress, port1);
        entity.WithId(id);
        entity.UpdateDeviceModel("S7");
        entity.UpdateProtocolFrame(null);
        entity.UpdateEndpoint(ipAddress, port1, null, 3000);
        entity.Enable();
        return entity;
    }

    private static NetworkDeviceDto CreateNetworkDto(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
        => new(
            id,
            name,
            DeviceType.PLC,
            "S7",
            ipAddress,
            port1,
            null,
            null,
            null,
            3000,
            true,
            null);

    private static IoMappingDto CreateIoMappingDto(
        int id,
        int deviceId,
        string signalKey,
        string plcAddress = "DB1.DBW0",
        string? remark = null)
        => new(
            id,
            deviceId,
            signalKey,
            plcAddress,
            1,
            "Int16",
            "Read",
            "单点读数据",
            string.Empty,
            1,
            remark);

    private static SerialDeviceEntity CreateSerialDevice(int id, string name)
    {
        var entity = SerialDeviceEntity.Create(name, "Scanner", "COM1", 9600);
        entity.WithId(id);
        entity.UpdatePort("COM1", 9600, 8, "One", "None");
        entity.Enable();
        return entity;
    }

    private static IoMappingEntity CreateIoMapping(
        int id,
        int deviceId,
        string signalKey,
        string plcAddress = "DB1.DBW0",
        string? remark = null)
    {
        var entity = IoMappingEntity.Create(deviceId, signalKey, plcAddress, 1, "Int16", "Read");
        entity.WithId(id);
        entity.UpdateSortOrder(1);
        entity.UpdateMetadata(signalKey, "Int16", "Read", "单点读数据", string.Empty, remark);
        return entity;
    }

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(x => x.Id) + 1;

        public IReadOnlyList<T> Items => _items;

        public IQueryable<T> GetQueryable() => _items.AsQueryable();

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(_items.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals((TKey)(object)x.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(expression));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

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
            => Task.FromResult(_items.AsQueryable().Count(expression));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                EntityIdTestHelper.SetId(entity, _nextId++);
            }

            _items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items[index] = entity;
            }
        }

        public void Delete(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var toDelete = _items.AsQueryable().Where(predicate).ToList();
            foreach (var item in toDelete)
            {
                _items.Remove(item);
            }

            return Task.FromResult(toDelete.Count);
        }
    }

    private sealed class HardwareConfigSender : ISender
    {
        public List<NetworkDeviceEntity> ExistingNetworkDevices { get; init; } = [];

        public List<IoMappingEntity> ExistingIoMappings { get; init; } = [];

        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            object response = request switch
            {
                GetAllNetworkDevicesQuery => Result.Success(ExistingNetworkDevices.Select(Clone).ToList()),
                GetIoMappingsByDeviceQuery query => Result.Success(new IoMappingPagedDto(
                    ExistingIoMappings
                        .Where(x => x.NetworkDeviceId == query.NetworkDeviceId)
                        .Select(Clone)
                        .ToList(),
                    ExistingIoMappings.Count(x => x.NetworkDeviceId == query.NetworkDeviceId))),
                SaveNetworkDevicesCommand => Result.Success(),
                SaveSerialDevicesCommand => Result.Success(),
                SaveIoMappingsCommand => Result.Success(),
                _ => throw new NotSupportedException(request.GetType().FullName)
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().FullName);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        private static NetworkDeviceEntity Clone(NetworkDeviceEntity entity)
        {
            var clone = NetworkDeviceEntity.Create(entity.DeviceName, entity.DeviceType, entity.IpAddress, entity.Port1);
            clone.WithId(entity.Id);
            clone.UpdateDeviceModel(entity.DeviceModel);
            clone.UpdateProtocolFrame(entity.ProtocolFrame);
            clone.UpdateEndpoint(entity.IpAddress, entity.Port1, entity.Port2, entity.ConnectTimeout);
            clone.UpdateCommands(entity.SendCmd1, entity.SendCmd2);
            clone.SetEnabled(entity.IsEnabled);
            clone.UpdateRemark(entity.Remark);
            return clone;
        }

        private static IoMappingEntity Clone(IoMappingEntity entity)
        {
            var clone = IoMappingEntity.Create(
                entity.NetworkDeviceId,
                entity.SignalKey,
                entity.PlcAddress,
                entity.AddressCount,
                entity.DataType,
                entity.Direction,
                entity.Category,
                entity.BusinessGroup);
            clone.WithId(entity.Id);
            clone.UpdateSortOrder(entity.SortOrder);
            clone.UpdateMetadata(
                entity.SignalKey,
                entity.DataType,
                entity.Direction,
                entity.Category,
                entity.BusinessGroup,
                entity.Remark);
            return clone;
        }
    }

    private sealed class FakePlcConnectionManager : IPlcConnectionManager
    {
        public Dictionary<int, Exception> StopFailures { get; } = [];

        public Dictionary<string, Exception> ReloadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<int> StoppedDeviceIds { get; } = [];

        public List<string> ReloadedDeviceNames { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default)
        {
            StoppedDeviceIds.Add(networkDeviceId);
            if (StopFailures.TryGetValue(networkDeviceId, out var exception))
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public Task ReloadAsync(string deviceName, CancellationToken ct = default)
        {
            ReloadedDeviceNames.Add(deviceName);
            if (ReloadFailures.TryGetValue(deviceName, out var exception))
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public void RegisterTasks(
            string deviceName,
            Func<IPlcBuffer, ProductionContext, List<IIoT.Edge.Application.Abstractions.Plc.IPlcTask>> factory)
        {
        }

        public IIoT.Edge.Application.Abstractions.Plc.IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => Array.Empty<PlcConnectionRuntimeSnapshot>();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
}
