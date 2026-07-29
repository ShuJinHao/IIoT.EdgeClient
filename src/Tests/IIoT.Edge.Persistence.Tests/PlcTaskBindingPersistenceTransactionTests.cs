using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace IIoT.Edge.Persistence.Tests;

public sealed class PlcTaskBindingPersistenceTransactionTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IIoT.Edge.Tests",
        "PlcBindingTransaction",
        Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Commit_WhenSqliteSaveFails_ShouldRollBackAndPreserveOriginalBinding()
    {
        var databasePath = Path.Combine(_root, "binding-commit-failure.db");
        var seedFactory = CreateFactory(databasePath);
        await using (var seed = seedFactory.CreateDbContext())
        {
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var device = NetworkDeviceEntity.Create(
                "PLC-Atomic",
                DeviceType.PLC,
                "127.0.0.1",
                6000);
            device.UpdateDeviceModel(PlcType.S7.ToString());
            seed.NetworkDevices.Add(device);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            seed.PlcTaskBindings.Add(PlcTaskBindingEntity.Create(
                device.Id,
                "Task.MG1",
                enabled: true,
                DateTimeOffset.UnixEpoch));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var primary = new IOException("deterministic sqlite save failure");
        var failingFactory = CreateFactory(
            databasePath,
            new ThrowingSaveChangesInterceptor(primary));
        var service = new PlcTaskBindingService(
            new SingleFactoryRuntimeRegistry(),
            new EfReadRepository<NetworkDeviceEntity>(failingFactory),
            new EfReadRepository<IoMappingEntity>(failingFactory),
            new EfReadRepository<PlcTaskBindingEntity>(failingFactory),
            new EdgeUnitOfWorkFactory(failingFactory));
        var savedDevice = await new EfReadRepository<NetworkDeviceEntity>(seedFactory)
            .GetAsync(
                static device => device.DeviceName == "PLC-Atomic",
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(savedDevice);

        var preparation = await service.PrepareAsync(
            savedDevice.Id,
            "TestModule",
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["Task.MG1"] = false
            },
            TestContext.Current.CancellationToken);
        var actual = await Assert.ThrowsAsync<IOException>(() =>
            service.CommitAsync(
                preparation,
                TestContext.Current.CancellationToken));

        Assert.Same(primary, actual);
        await using var verify = seedFactory.CreateDbContext();
        var row = await verify.PlcTaskBindings.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal("Task.MG1", row.TaskKey);
        Assert.True(row.Enabled);
        Assert.Equal(DateTimeOffset.UnixEpoch, row.UpdatedAt);
    }

    private static IDbContextFactory<EdgeDbContext> CreateFactory(
        string databasePath,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new PooledDbContextFactory<EdgeDbContext>(options.Options);
    }

    private sealed class ThrowingSaveChangesInterceptor(Exception exception)
        : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(exception);
    }

    private sealed class SingleFactoryRuntimeRegistry : IStationRuntimeRegistry
    {
        private readonly IStationRuntimeFactory _factory = new SingleTaskRuntimeFactory();

        public void Register(IStationRuntimeFactory runtimeFactory)
        {
        }

        public bool HasFactory(string moduleId)
            => string.Equals(moduleId, _factory.ModuleId, StringComparison.OrdinalIgnoreCase);

        public bool TryGetFactory(
            string moduleId,
            out IStationRuntimeFactory runtimeFactory)
        {
            if (HasFactory(moduleId))
            {
                runtimeFactory = _factory;
                return true;
            }

            runtimeFactory = null!;
            return false;
        }

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations()
            => new Dictionary<string, IStationRuntimeFactory>(StringComparer.OrdinalIgnoreCase)
            {
                [_factory.ModuleId] = _factory
            };
    }

    private sealed class SingleTaskRuntimeFactory : IStationRuntimeFactory
    {
        public string ModuleId => "TestModule";

        public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
            => [new TaskCandidate("Task.MG1", "MG1", [])];

        public List<IPlcTask> CreateTasks(
            IServiceProvider serviceProvider,
            IPlcBuffer buffer,
            ProductionContext context,
            IReadOnlySet<string> enabledTaskKeys)
            => [];
    }
}
