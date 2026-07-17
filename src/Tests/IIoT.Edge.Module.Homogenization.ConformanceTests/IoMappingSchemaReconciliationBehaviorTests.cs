using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using IIoT.Edge.Testing;

namespace IIoT.Edge.Module.Homogenization.ConformanceTests;

public sealed class IoMappingSchemaReconciliationBehaviorTests
{
    [Fact]
    public async Task ReconcileAsync_WhenIoSchemaRegistered_ShouldSeedMissingDefaultsDeleteStaleAndPreserveExisting()
    {
        var device = CreatePlc(7, "PLC-Homogenization");
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(device);
        var customizedInbound = CreateMapping(
            10,
            device.Id,
            "Homogenization.Interaction.Inbound",
            "D999",
            "Read",
            sortOrder: 2);
        var staleMapping = CreateMapping(11, device.Id, "Legacy.Signal", "D1", "Read", sortOrder: 900);
        var ioRepo = new InMemoryRepository<IoMappingEntity>(customizedInbound, staleMapping);
        var profiles = new IModuleHardwareProfileProvider[] { new HomogenizationHardwareProfileProvider() };
        var profileResolver = new ModuleHardwareProfileResolver(profiles);
        var reconciler = new ConfigSchemaReconciler(
            [new IoMappingSchemaSource(networkRepo, profileResolver)],
            [new IoMappingConfigValueStore(
                networkRepo,
                ioRepo,
                new TestEdgeUnitOfWorkFactory(ioRepo),
                profileResolver)]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var candidates = profiles[0].GetIoMappingCandidates();
        var managedMappings = ioRepo.Items
            .Where(x => x.NetworkDeviceId == device.Id)
            .ToArray();
        Assert.Equal(candidates.Count, managedMappings.Length);
        Assert.DoesNotContain(ioRepo.Items, x => x.NetworkDeviceId == device.Id && x.SignalKey == "Legacy.Signal");
        Assert.Contains(ioRepo.Items, x =>
            x.NetworkDeviceId == device.Id
            && x.SignalKey == "Homogenization.Interaction.Inbound"
            && x.Direction == "Read"
            && x.PlcAddress == "D999");
        Assert.DoesNotContain(ioRepo.Items, x => string.IsNullOrWhiteSpace(x.PlcAddress));
    }

    [Fact]
    public async Task ReconcileAsync_WhenRemarkMatchesModuleLegacyAlias_ShouldMigrateOnlyThatExactRemark()
    {
        var device = CreatePlc(8, "PLC-Homogenization");
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(device);
        var profile = new HomogenizationHardwareProfileProvider();
        var candidates = profile.GetIoMappingCandidates();
        var legacyCandidate = candidates.First(static x => x.LegacyRemarks is { Count: > 0 });
        var manualCandidate = candidates.First(x =>
            !string.Equals(x.SignalKey, legacyCandidate.SignalKey, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(x.Direction, legacyCandidate.Direction, StringComparison.OrdinalIgnoreCase));
        var legacyMapping = CreateMapping(
            20,
            device.Id,
            legacyCandidate.SignalKey,
            "D999",
            legacyCandidate.Direction,
            legacyCandidate.SortOrder,
            legacyCandidate.LegacyRemarks![0]);
        var manualMapping = CreateMapping(
            21,
            device.Id,
            manualCandidate.SignalKey,
            "D998",
            manualCandidate.Direction,
            manualCandidate.SortOrder,
            "现场自定义备注");
        var ioRepo = new InMemoryRepository<IoMappingEntity>(legacyMapping, manualMapping);
        var profileResolver = new ModuleHardwareProfileResolver([profile]);
        var reconciler = new ConfigSchemaReconciler(
            [new IoMappingSchemaSource(networkRepo, profileResolver)],
            [new IoMappingConfigValueStore(
                networkRepo,
                ioRepo,
                new TestEdgeUnitOfWorkFactory(ioRepo),
                profileResolver)]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);
        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        Assert.Equal(legacyCandidate.Remark, legacyMapping.Remark);
        Assert.Equal("D999", legacyMapping.PlcAddress);
        Assert.Equal("现场自定义备注", manualMapping.Remark);
        Assert.Equal("D998", manualMapping.PlcAddress);
    }

    private static NetworkDeviceEntity CreatePlc(int id, string deviceName)
    {
        var entity = NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 6000);
        entity.WithId(id);
        entity.UpdateDeviceModel(PlcType.Mc.ToString());
        return entity;
    }

    private static IoMappingEntity CreateMapping(
        int id,
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        string direction,
        int sortOrder,
        string? remark = null)
    {
        var entity = IoMappingEntity.Create(
            networkDeviceId,
            signalKey,
            plcAddress,
            1,
            "Int16",
            direction,
            "信号交互",
            "测试");
        entity.WithId(id);
        entity.UpdateSortOrder(sortOrder);
        if (remark is not null)
        {
            entity.UpdateMetadata(
                entity.SignalKey,
                entity.DataType,
                entity.Direction,
                entity.Category,
                entity.BusinessGroup,
                remark);
        }

        return entity;
    }

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(static x => x.Id) + 1;

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

        public void Delete(T entity) => _items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var toDelete = _items.AsQueryable().Where(predicate).ToArray();
            foreach (var item in toDelete)
            {
                _items.Remove(item);
            }

            return Task.FromResult(toDelete.Length);
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
    }
}
