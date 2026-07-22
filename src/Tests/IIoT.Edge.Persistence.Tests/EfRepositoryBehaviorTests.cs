using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IIoT.Edge.Persistence.Tests;

public sealed class EfRepositoryBehaviorTests
{
    [Fact]
    public async Task Update_WhenEntityExists_ShouldPersistChangedFieldsAndMaterializeProtectedId()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var entity = NetworkDeviceEntity.Create(
                "PLC-A",
                DeviceType.PLC,
                "127.0.0.1",
                6000);
            entity.UpdateDeviceModel("MC");
            entity.UpdateRemark("old");

            await using (var unitOfWork = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken))
            {
                unitOfWork.Repository<NetworkDeviceEntity>().Add(entity);
                await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
            }

            var id = entity.Id;
            Assert.True(id > 0);

            await using var updateUnitOfWork = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken);
            var repository = updateUnitOfWork.Repository<NetworkDeviceEntity>();
            var loaded = await repository.GetByIdAsync<int>(id, TestContext.Current.CancellationToken);
            Assert.NotNull(loaded);
            Assert.Equal(id, loaded!.Id);
            Assert.Equal("PLC-A", loaded.PlcCode);

            loaded.Rename("PLC-B");
            loaded.UpdateEndpoint("192.168.0.10", 5001, null, 4500);
            loaded.UpdateRemark("new");
            loaded.Disable();

            repository.Update(loaded);
            await updateUnitOfWork.CommitAsync(TestContext.Current.CancellationToken);

            await using var readUnitOfWork = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken);
            var updated = await readUnitOfWork.Repository<NetworkDeviceEntity>()
                .GetByIdAsync<int>(id, TestContext.Current.CancellationToken);

            Assert.NotNull(updated);
            Assert.Equal(id, updated!.Id);
            Assert.Equal("PLC-B", updated.DeviceName);
            Assert.Equal("PLC-A", updated.PlcCode);
            Assert.Equal("192.168.0.10", updated.IpAddress);
            Assert.Equal(5001, updated.Port1);
            Assert.Equal(4500, updated.ConnectTimeout);
            Assert.Equal("new", updated.Remark);
            Assert.False(updated.IsEnabled);
        }
        finally
        {
            database.Dispose();
        }
    }

    [Fact]
    public async Task Update_WhenEntityDoesNotExist_ShouldReject()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            await using var unitOfWork = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken);
            var repository = unitOfWork.Repository<NetworkDeviceEntity>();
            var entity = NetworkDeviceEntity.Create(
                    "PLC-Missing",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6000)
                .WithId(999);
            entity.UpdateDeviceModel("MC");

            var exception = Assert.Throws<InvalidOperationException>(() => repository.Update(entity));

            Assert.Contains("无法更新不存在的实体", exception.Message);
        }
        finally
        {
            database.Dispose();
        }
    }

    [Fact]
    public async Task QuerySurface_ShouldMatchForReadAndUnitOfWorkRepositories()
    {
        var database = await CreateDatabaseAsync();
        try
        {
            var device = NetworkDeviceEntity.Create(
                "PLC-Query",
                DeviceType.PLC,
                "127.0.0.1",
                6000);
            await using (var seed = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken))
            {
                seed.Repository<NetworkDeviceEntity>().Add(device);
                await seed.FlushAsync(TestContext.Current.CancellationToken);
                seed.Repository<IoMappingEntity>().Add(
                    IoMappingEntity.Create(
                        device.Id,
                        "Query.Signal",
                        "D0",
                        1,
                        "Int16",
                        "Read"));
                seed.Repository<PlcTaskBindingEntity>().Add(
                    PlcTaskBindingEntity.Create(
                        device.Id,
                        "Query.Task",
                        true,
                        DateTimeOffset.UtcNow));
                await seed.CommitAsync(TestContext.Current.CancellationToken);
            }

            var readRepository = new EfReadRepository<NetworkDeviceEntity>(database.ContextFactory);
            await AssertQuerySurfaceAsync(readRepository, device.Id);

            await using var queryUnitOfWork = await database.UnitOfWorkFactory.BeginAsync(TestContext.Current.CancellationToken);
            await AssertQuerySurfaceAsync(
                queryUnitOfWork.Repository<NetworkDeviceEntity>(),
                device.Id);
        }
        finally
        {
            database.Dispose();
        }

        static async Task AssertQuerySurfaceAsync(
            IReadRepository<NetworkDeviceEntity> repository,
            int deviceId)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var specification = new NetworkDeviceByNameSpecification("PLC-Query");

            Assert.NotNull(await repository.GetByIdAsync(deviceId, cancellationToken));
            Assert.NotNull(await repository.GetAsync(
                static item => item.DeviceName == "PLC-Query",
                includes: null,
                cancellationToken));
            Assert.Single(await repository.GetListAsync(
                static item => item.DeviceName == "PLC-Query",
                cancellationToken));
            var included = Assert.Single(await repository.GetListAsync(
                static item => item.DeviceName == "PLC-Query",
                [static item => item.IoMappings, static item => item.PlcTaskBindings],
                cancellationToken));
            Assert.Single(included.IoMappings);
            Assert.Single(included.PlcTaskBindings);
            Assert.Single(await repository.GetListAsync(specification, cancellationToken));
            Assert.NotNull(await repository.GetSingleOrDefaultAsync(specification, cancellationToken));
            Assert.Equal(1, await repository.GetCountAsync(
                static item => item.DeviceName == "PLC-Query",
                cancellationToken));
            Assert.Equal(1, await repository.CountAsync(specification, cancellationToken));
            Assert.True(await repository.AnyAsync(specification, cancellationToken));
        }
    }

    private static async Task<RepositoryDatabase> CreateDatabaseAsync()
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            "IIoT.Edge.Tests",
            $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);

        using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        return new RepositoryDatabase(
            dbPath,
            factory,
            new EdgeUnitOfWorkFactory(factory));
    }

    private sealed class RepositoryDatabase(
        string dbPath,
        IDbContextFactory<EdgeDbContext> contextFactory,
        IEdgeUnitOfWorkFactory unitOfWorkFactory) : IDisposable
    {
        public IDbContextFactory<EdgeDbContext> ContextFactory { get; } = contextFactory;

        public IEdgeUnitOfWorkFactory UnitOfWorkFactory { get; } = unitOfWorkFactory;

        public void Dispose()
        {
            foreach (var path in new[]
                     {
                         dbPath,
                         dbPath + "-wal",
                         dbPath + "-shm"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<EdgeDbContext> options) : IDbContextFactory<EdgeDbContext>
    {
        public EdgeDbContext CreateDbContext()
            => new(options);
    }

    private sealed class NetworkDeviceByNameSpecification : Specification<NetworkDeviceEntity>
    {
        public NetworkDeviceByNameSpecification(string deviceName)
        {
            FilterCondition = item => item.DeviceName == deviceName;
        }
    }
}
