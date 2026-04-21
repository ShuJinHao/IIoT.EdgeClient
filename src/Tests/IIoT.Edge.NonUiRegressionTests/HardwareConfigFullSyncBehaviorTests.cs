using System.Linq.Expressions;
using AutoMapper;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HardwareConfigFullSyncBehaviorTests
{
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
                        "Injection",
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
            CreateIoMapping(id: 1, deviceId: 9, label: "Signal.A", remark: "keep"),
            CreateIoMapping(id: 2, deviceId: 9, label: "Signal.B", remark: "delete"),
            CreateIoMapping(id: 3, deviceId: 10, label: "Signal.C", remark: "other-device"));
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
                [CreateNetworkVm(id: 2, name: "PLC-B")],
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
                CreateIoMapping(id: 11, deviceId: 1, label: "Signal.A", plcAddress: "DB1.DBW0", remark: "old")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkVm(id: 1, name: "PLC-A"), CreateNetworkVm(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    new IoMappingVm
                    {
                        Id = 11,
                        NetworkDeviceId = 1,
                        Label = "Signal.A",
                        PlcAddress = "DB1.DBW0",
                        AddressCount = 1,
                        DataType = "Int16",
                        Direction = "Read",
                        SortOrder = 1,
                        Remark = "new"
                    }
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
                CreateIoMapping(id: 11, deviceId: 1, label: "Signal.A", plcAddress: "DB1.DBW0", remark: "same")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkVm(id: 1, name: "PLC-A"), CreateNetworkVm(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    new IoMappingVm
                    {
                        Id = 11,
                        NetworkDeviceId = 1,
                        Label = "Signal.A",
                        PlcAddress = "DB1.DBW0",
                        AddressCount = 1,
                        DataType = "Int16",
                        Direction = "Read",
                        SortOrder = 1,
                        Remark = "same"
                    }
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
                    CreateNetworkVm(id: 1, name: "PLC-DUP", ipAddress: "192.168.0.10", port1: 103),
                    CreateNetworkVm(id: 2, name: "PLC-DUP", ipAddress: "192.168.0.11", port1: 104)
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
                    CreateNetworkVm(id: 2, name: "PLC-B", port1: 103)
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
            CreateMapper(),
            new StubPermissionService { CanEditHardware = true },
            plcManager);

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Mappings.HardwareConfigMappingProfile>(),
            NullLoggerFactory.Instance);

        return configuration.CreateMapper();
    }

    private static NetworkDeviceEntity CreateNetworkDevice(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
        => new(name, DeviceType.PLC, ipAddress, port1)
        {
            Id = id,
            DeviceModel = "S7",
            ModuleId = "Injection",
            ConnectTimeout = 3000,
            IsEnabled = true
        };

    private static NetworkDeviceVm CreateNetworkVm(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
        => new()
        {
            Id = id,
            DeviceName = name,
            DeviceType = DeviceType.PLC,
            DeviceModel = "S7",
            ModuleId = "Injection",
            IpAddress = ipAddress,
            Port1 = port1,
            ConnectTimeout = 3000,
            IsEnabled = true
        };

    private static SerialDeviceEntity CreateSerialDevice(int id, string name)
        => new(name, "Scanner", "COM1", 9600)
        {
            Id = id,
            DataBits = 8,
            StopBits = "One",
            Parity = "None",
            IsEnabled = true
        };

    private static IoMappingEntity CreateIoMapping(
        int id,
        int deviceId,
        string label,
        string plcAddress = "DB1.DBW0",
        string? remark = null)
        => new(deviceId, label, plcAddress, 1, "Int16", "Read")
        {
            Id = id,
            SortOrder = 1,
            Remark = remark
        };

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
                entity.Id = _nextId++;
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
            => new(entity.DeviceName, entity.DeviceType, entity.IpAddress, entity.Port1)
            {
                Id = entity.Id,
                DeviceModel = entity.DeviceModel,
                ModuleId = entity.ModuleId,
                Port2 = entity.Port2,
                SendCmd1 = entity.SendCmd1,
                SendCmd2 = entity.SendCmd2,
                ConnectTimeout = entity.ConnectTimeout,
                IsEnabled = entity.IsEnabled,
                Remark = entity.Remark
            };

        private static IoMappingEntity Clone(IoMappingEntity entity)
            => new(entity.NetworkDeviceId, entity.Label, entity.PlcAddress, entity.AddressCount, entity.DataType, entity.Direction)
            {
                Id = entity.Id,
                SortOrder = entity.SortOrder,
                Remark = entity.Remark
            };
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
