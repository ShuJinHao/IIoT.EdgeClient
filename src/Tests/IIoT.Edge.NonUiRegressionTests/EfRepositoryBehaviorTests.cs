using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EfRepositoryBehaviorTests
{
    [Fact]
    public async Task Update_WhenEntityExists_ShouldPersistChangedFieldsAndMaterializeProtectedId()
    {
        var database = await CreateRepositoryAsync<NetworkDeviceEntity>();
        try
        {
            var repository = database.Repository;
            var entity = NetworkDeviceEntity.Create(
                "PLC-A",
                DeviceType.PLC,
                "127.0.0.1",
                6000);
            entity.UpdateDeviceModel("MC");
            entity.UpdateRemark("old");

            repository.Add(entity);
            await repository.SaveChangesAsync();

            var id = entity.Id;
            Assert.True(id > 0);

            var loaded = await repository.GetByIdAsync<int>(id);
            Assert.NotNull(loaded);
            Assert.Equal(id, loaded!.Id);

            loaded.Rename("PLC-B");
            loaded.UpdateEndpoint("192.168.0.10", 5001, null, 4500);
            loaded.UpdateRemark("new");
            loaded.Disable();

            repository.Update(loaded);
            await repository.SaveChangesAsync();

            var updated = await repository.GetByIdAsync<int>(id);

            Assert.NotNull(updated);
            Assert.Equal(id, updated!.Id);
            Assert.Equal("PLC-B", updated.DeviceName);
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
        var database = await CreateRepositoryAsync<NetworkDeviceEntity>();
        try
        {
            var repository = database.Repository;
            var entity = NetworkDeviceEntity.Create(
                    "PLC-Missing",
                    DeviceType.PLC,
                    "127.0.0.1",
                    6000)
                .WithId(999);
            entity.UpdateDeviceModel("MC");

            repository.Update(entity);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SaveChangesAsync());

            Assert.Contains("无法更新不存在的实体", exception.Message);
        }
        finally
        {
            database.Dispose();
        }
    }

    private static async Task<RepositoryDatabase<TEntity>> CreateRepositoryAsync<TEntity>()
        where TEntity : class, IEntity, IAggregateRoot
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

        return new RepositoryDatabase<TEntity>(
            dbPath,
            new EfRepository<TEntity>(factory));
    }

    private sealed class RepositoryDatabase<TEntity>(
        string dbPath,
        EfRepository<TEntity> repository) : IDisposable
        where TEntity : class, IEntity, IAggregateRoot
    {
        public EfRepository<TEntity> Repository { get; } = repository;

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
}
