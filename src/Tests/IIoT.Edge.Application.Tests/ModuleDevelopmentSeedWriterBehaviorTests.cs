using System.Linq.Expressions;
using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;

namespace IIoT.Edge.Application.Tests;

public sealed class ModuleDevelopmentSeedWriterBehaviorTests
{
    [Fact]
    public async Task ApplyAsync_WhenSeedIsNew_ShouldMaterializeOnceAndRemainIdempotent()
    {
        var devices = new InMemoryRepository<NetworkDeviceEntity>();
        var mappings = new InMemoryRepository<IoMappingEntity>();
        var unitOfWorkFactory = new TestEdgeUnitOfWorkFactory(devices, mappings);
        var writer = new ModuleDevelopmentSeedWriter(unitOfWorkFactory);
        var request = CreateRequest(resetBeforeImport: false);

        var first = await writer.ApplyAsync(request, TestContext.Current.CancellationToken);
        var second = await writer.ApplyAsync(request, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices.Items);
        Assert.Equal("PLC-Test-01", device.DeviceName);
        Assert.Equal("PLC-TEST-01", device.PlcCode);
        Assert.Equal("Mc", device.DeviceModel);
        Assert.Null(device.ProtocolFrame);
        Assert.Equal(2, mappings.Items.Count);
        Assert.Contains(mappings.Items, static mapping =>
            mapping.SignalKey == "TestModule.SignalA"
            && mapping.PlcAddress == "D100"
            && mapping.SortOrder == 1);
        Assert.Equal(new ModuleDevelopmentSeedResult(1, 2, 0, 0), first);
        Assert.Equal(new ModuleDevelopmentSeedResult(0, 0, 0, 0), second);
        Assert.Equal(2, unitOfWorkFactory.BeginCount);
        Assert.Equal(2, unitOfWorkFactory.CommitCount);
    }

    [Fact]
    public async Task ApplyAsync_WhenSeedProvidesStableIdentityAndProtocolFrame_ShouldPersistBoth()
    {
        var devices = new InMemoryRepository<NetworkDeviceEntity>();
        var mappings = new InMemoryRepository<IoMappingEntity>();
        var writer = new ModuleDevelopmentSeedWriter(new TestEdgeUnitOfWorkFactory(devices, mappings));
        var template = Assert.Single(CreateRequest(resetBeforeImport: false).Devices);
        var request = new ModuleDevelopmentSeedRequest(
            "CP",
            ResetBeforeImport: false,
            [
                template with
                {
                    DeviceName = "正极模切01",
                    PlcCode = "P2-CP01",
                    ProtocolFrame = "E4"
                }
            ]);

        var result = await writer.ApplyAsync(request, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices.Items);
        Assert.Equal("正极模切01", device.DeviceName);
        Assert.Equal("P2-CP01", device.PlcCode);
        Assert.Equal("Mc", device.DeviceModel);
        Assert.Equal("E4", device.ProtocolFrame);
        Assert.Equal(new ModuleDevelopmentSeedResult(1, 2, 0, 0), result);
    }

    [Fact]
    public async Task ApplyAsync_WhenDeviceAlreadyHasMappings_ShouldPreserveOperatorValues()
    {
        var existingDevice = CreateDevice("PLC-Test-01", "D999", plcCode: "SITE-PLC-01");
        existingDevice.UpdateProtocolFrame("E3");
        var existingMapping = IoMappingEntity.Create(
            existingDevice.Id,
            "TestModule.SignalA",
            "D999",
            1,
            "Int16",
            "Read",
            "单点读数据",
            "现场自定义").WithId(7);
        var devices = new InMemoryRepository<NetworkDeviceEntity>(existingDevice);
        var mappings = new InMemoryRepository<IoMappingEntity>(existingMapping);
        var writer = new ModuleDevelopmentSeedWriter(new TestEdgeUnitOfWorkFactory(devices, mappings));

        var result = await writer.ApplyAsync(
            CreateRequest(resetBeforeImport: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(new ModuleDevelopmentSeedResult(0, 0, 0, 0), result);
        Assert.Equal("D999", Assert.Single(mappings.Items).PlcAddress);
        Assert.Equal("SITE-PLC-01", Assert.Single(devices.Items).PlcCode);
        Assert.Equal("E3", Assert.Single(devices.Items).ProtocolFrame);
    }

    [Fact]
    public async Task ApplyAsync_WhenResetRequested_ShouldReplaceAllHostDeviceAndMappingRows()
    {
        var firstDevice = CreateDevice("PLC-Old-A", "D10");
        var secondDevice = CreateDevice("PLC-Old-B", "D20", id: 2);
        var devices = new InMemoryRepository<NetworkDeviceEntity>(firstDevice, secondDevice);
        var mappings = new InMemoryRepository<IoMappingEntity>(
            IoMappingEntity.Create(firstDevice.Id, "Old.A", "D10", 1, "Int16", "Read").WithId(10),
            IoMappingEntity.Create(secondDevice.Id, "Old.B", "D20", 1, "Int16", "Read").WithId(11));
        var writer = new ModuleDevelopmentSeedWriter(new TestEdgeUnitOfWorkFactory(devices, mappings));

        var result = await writer.ApplyAsync(
            CreateRequest(resetBeforeImport: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(new ModuleDevelopmentSeedResult(1, 2, 2, 2), result);
        Assert.Equal("PLC-Test-01", Assert.Single(devices.Items).DeviceName);
        Assert.DoesNotContain(mappings.Items, static mapping => mapping.SignalKey.StartsWith("Old.", StringComparison.Ordinal));
        Assert.Equal(2, mappings.Items.Count);
    }

    private static ModuleDevelopmentSeedRequest CreateRequest(bool resetBeforeImport)
        => new(
            "TestModule",
            resetBeforeImport,
            [
                new ModuleDevelopmentDeviceSeed(
                    "PLC-Test-01",
                    "Mc",
                    "127.0.0.1",
                    6000,
                    3000,
                    true,
                    "测试模块 PLC",
                    [
                        new ModuleIoTemplateEntry(
                            "TestModule.SignalB",
                            "D200",
                            1,
                            "Int16",
                            "Write",
                            2,
                            "信号 B"),
                        new ModuleIoTemplateEntry(
                            "TestModule.SignalA",
                            "D100",
                            1,
                            "Int16",
                            "Read",
                            1,
                            "信号 A"),
                        new ModuleIoTemplateEntry(
                            "TestModule.Unconfigured",
                            string.Empty,
                            1,
                            "Int16",
                            "Read",
                            3,
                            "未配置地址")
                    ])
            ]);

    private static NetworkDeviceEntity CreateDevice(
        string name,
        string markerAddress,
        int id = 1,
        string? plcCode = null)
    {
        var device = NetworkDeviceEntity.Create(name, DeviceType.PLC, "127.0.0.1", 6000, plcCode).WithId(id);
        device.UpdateDeviceModel("Mc");
        device.UpdateRemark(markerAddress);
        return device;
    }

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(static item => item.Id) + 1;

        public IReadOnlyList<T> Items => _items;

        public IQueryable<T> GetQueryable() => _items.AsQueryable();

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
        }

        public void Delete(T entity) => _items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var deleted = _items.RemoveAll(item => predicate.Compile()(item));
            return Task.FromResult(deleted);
        }

        public async Task<int> ReplaceAsync(
            Expression<Func<T, bool>> predicate,
            IReadOnlyCollection<T> replacements,
            CancellationToken cancellationToken = default)
        {
            var affected = await ExecuteDeleteAsync(predicate, cancellationToken);
            foreach (var replacement in replacements)
            {
                Add(replacement);
                affected++;
            }

            return affected;
        }

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(_items.FirstOrDefault(item => EqualityComparer<object>.Default.Equals(item.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(expression.Compile()));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Where(expression.Compile()).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.Where(expression.Compile()).ToList());

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
            => Task.FromResult(_items.Count(expression.Compile()));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
