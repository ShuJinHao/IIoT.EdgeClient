using IIoT.Edge.Module.Contracts.Cache;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;

namespace IIoT.Edge.Persistence.Tests;

public sealed class EfRepositoryIsolationAndAtomicityTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IIoT.Edge.Tests",
        "RepositoryIsolation",
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
    public async Task ConcurrentCallers_ShouldUseSerializedIndependentUnitsOfWork()
    {
        var contextFactory = await CreateFactoryAsync("caller-isolation.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);

        await using var callerA = await factory.BeginAsync(TestContext.Current.CancellationToken);
        callerA.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Caller:A", "1"));
        var callerBAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerB = Task.Run(async () =>
        {
            callerBAttempted.SetResult();
            await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
            unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Caller:B", "1"));
            await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        await callerBAttempted.Task.WaitAsync(TestContext.Current.CancellationToken);
        await callerA.CommitAsync(TestContext.Current.CancellationToken);
        await callerB;
        await using var verifyAll = contextFactory.CreateDbContext();
        Assert.Equal(
            ["Caller:A", "Caller:B"],
            await verifyAll.SystemConfigs
                .OrderBy(static item => item.Key)
                .Select(static item => item.Key)
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeWithoutCommit_AfterFlush_ShouldRollBackAllWrites()
    {
        var contextFactory = await CreateFactoryAsync("rollback-on-dispose.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);

        await using (var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken))
        {
            unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Pending", "1"));
            Assert.Equal(1, await unitOfWork.FlushAsync(TestContext.Current.CancellationToken));
        }

        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(await verify.SystemConfigs.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_ShouldPersistDifferentAggregateTypesAtomically()
    {
        var contextFactory = await CreateFactoryAsync("cross-aggregate-commit.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);

        await using (var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken))
        {
            unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Atomic:Config", "1"));
            unitOfWork.Repository<NetworkDeviceEntity>().Add(
                NetworkDeviceEntity.Create("PLC-Atomic", DeviceType.PLC, "127.0.0.1", 6000));
            await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = contextFactory.CreateDbContext();
        Assert.True(await verify.SystemConfigs.AnyAsync(
            static item => item.Key == "Atomic:Config",
            TestContext.Current.CancellationToken));
        Assert.True(await verify.NetworkDevices.AnyAsync(
            static item => item.DeviceName == "PLC-Atomic",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnitOfWork_ShouldReuseRepositoryOnlyInsideTheSameSession()
    {
        var contextFactory = await CreateFactoryAsync("repository-scope.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);
        object firstRepository;
        await using (var first = await factory.BeginAsync(TestContext.Current.CancellationToken))
        {
            firstRepository = first.Repository<SystemConfigEntity>();
            Assert.Same(firstRepository, first.Repository<SystemConfigEntity>());
            await first.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using var second = await factory.BeginAsync(TestContext.Current.CancellationToken);
        Assert.NotSame(firstRepository, second.Repository<SystemConfigEntity>());
    }

    [Fact]
    public async Task Commit_WhenCalledTwice_ShouldRejectSecondCommit()
    {
        var contextFactory = await CreateFactoryAsync("one-shot-commit.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);
        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Once", "1"));

        await unitOfWork.CommitAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Committed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnitOfWork_ShouldEnableForeignKeyChecksOnItsOwnConnection()
    {
        var contextFactory = await CreateFactoryAsync("foreign-key.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);
        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        unitOfWork.Repository<IoMappingEntity>().Add(
            IoMappingEntity.Create(999, "Missing.Device", "D0", 1, "Int16", "Read"));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InsertSystemConfigAsync_WhenReplacementInsertFails_ShouldPreserveOldRow()
    {
        var seedFactory = await CreateFactoryAsync("system-config-replace.db");
        await using (var seed = seedFactory.CreateDbContext())
        {
            seed.SystemConfigs.Add(SystemConfigEntity.Create("Module:Test:Value", "old"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var failingContextFactory = CreateFactory(
            "system-config-replace.db",
            new FailOnceSaveChangesInterceptor());
        var service = new LocalParameterConfigService(
            new EfReadRepository<SystemConfigEntity>(failingContextFactory),
            new EdgeUnitOfWorkFactory(failingContextFactory),
            new NoopCache());

        await Assert.ThrowsAsync<IOException>(() => service.InsertSystemConfigAsync(
            "Module:Test:Value",
            "new",
            cancellationToken: TestContext.Current.CancellationToken));

        await using var verify = seedFactory.CreateDbContext();
        Assert.Equal(
            ["old"],
            await verify.SystemConfigs
                .Where(static config => config.Key == "Module:Test:Value")
                .Select(static config => config.Value)
                .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_WhenSaveAndRollbackFail_ShouldRethrowPrimaryAndAttachRollbackFailure()
    {
        var contextFactory = await CreateFactoryAsync("rollback-primary-error.db");
        var primary = new IOException("primary database failure");
        var rollback = new InvalidOperationException("rollback failure");
        var failingContextFactory = CreateFactory(
            "rollback-primary-error.db",
            new ThrowingSaveChangesInterceptor(primary),
            new ThrowingRollbackInterceptor(rollback));
        var factory = new EdgeUnitOfWorkFactory(failingContextFactory);

        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Primary", "1"));
        var actual = await Assert.ThrowsAsync<IOException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));

        Assert.Same(primary, actual);
        Assert.Same(rollback, actual.Data["IIoT.Edge.Persistence.RollbackException"]);
        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(await verify.SystemConfigs.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Flush_WhenSaveFails_ShouldFaultSessionRejectFurtherWritesAndCommitAndPersistNothing()
    {
        var verifyFactory = await CreateFactoryAsync("flush-primary-error.db");
        var primary = new IOException("flush database failure");
        var failingContextFactory = CreateFactory(
            "flush-primary-error.db",
            new ThrowingSaveChangesInterceptor(primary));
        var factory = new EdgeUnitOfWorkFactory(failingContextFactory);

        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        var repository = unitOfWork.Repository<SystemConfigEntity>();
        repository.Add(SystemConfigEntity.Create("Flush:Primary", "1"));

        var actual = await Assert.ThrowsAsync<IOException>(
            () => unitOfWork.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Same(primary, actual);
        var writeFailure = Assert.Throws<InvalidOperationException>(
            () => repository.Add(SystemConfigEntity.Create("Flush:Rejected", "2")));
        Assert.Contains("Faulted", writeFailure.Message, StringComparison.Ordinal);
        var commitFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Faulted", commitFailure.Message, StringComparison.Ordinal);
        await using var verify = verifyFactory.CreateDbContext();
        Assert.Empty(await verify.SystemConfigs.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Flush_WhenSaveAndRollbackFail_ShouldRethrowPrimaryAndAttachRollbackFailure()
    {
        var verifyFactory = await CreateFactoryAsync("flush-rollback-primary-error.db");
        var primary = new IOException("flush primary database failure");
        var rollback = new InvalidOperationException("flush rollback failure");
        var failingContextFactory = CreateFactory(
            "flush-rollback-primary-error.db",
            new ThrowingSaveChangesInterceptor(primary),
            new ThrowingRollbackInterceptor(rollback));
        var factory = new EdgeUnitOfWorkFactory(failingContextFactory);

        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Flush:Rollback", "1"));
        var actual = await Assert.ThrowsAsync<IOException>(
            () => unitOfWork.FlushAsync(TestContext.Current.CancellationToken));

        Assert.Same(primary, actual);
        Assert.Same(rollback, actual.Data["IIoT.Edge.Persistence.RollbackException"]);
        var commitFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Faulted", commitFailure.Message, StringComparison.Ordinal);
        await using var verify = verifyFactory.CreateDbContext();
        Assert.Empty(await verify.SystemConfigs.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_WhenCallerIsPreCanceled_ShouldRollbackAndRethrowOriginalToken()
    {
        var contextFactory = await CreateFactoryAsync("commit-cancel.db");
        var factory = new EdgeUnitOfWorkFactory(contextFactory);
        await using var unitOfWork = await factory.BeginAsync(TestContext.Current.CancellationToken);
        unitOfWork.Repository<SystemConfigEntity>().Add(SystemConfigEntity.Create("Cancel:Commit", "1"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => unitOfWork.CommitAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        var secondCommit = await Assert.ThrowsAsync<InvalidOperationException>(
            () => unitOfWork.CommitAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Faulted", secondCommit.Message, StringComparison.Ordinal);
        await using var verify = contextFactory.CreateDbContext();
        Assert.Empty(await verify.SystemConfigs.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    private async Task<IDbContextFactory<EdgeDbContext>> CreateFactoryAsync(
        string fileName,
        params IInterceptor[] interceptors)
    {
        var factory = CreateFactory(fileName, interceptors);
        await using var db = factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        return factory;
    }

    private IDbContextFactory<EdgeDbContext> CreateFactory(
        string fileName,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<EdgeDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, fileName),
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

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _remainingFailures = 1;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
            {
                throw new IOException("deterministic insert failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowingSaveChangesInterceptor(Exception exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(exception);
    }

    private sealed class ThrowingRollbackInterceptor(Exception exception) : DbTransactionInterceptor
    {
        public override Task TransactionRolledBackAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
            => Task.FromException(exception);
    }

    private sealed class NoopCache : IEdgeCacheService
    {
        public T? Get<T>(string key) => default;
        public void Set<T>(string key, T value) { }
        public void Remove(string key) { }
        public void RemoveByPrefix(string prefix) { }
        public void Clear() { }
        public bool Contains(string key) => false;

        public Task<T?> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T?>> factory,
            TimeSpan? absoluteExpirationRelativeToNow = null,
            TimeSpan? nullValueExpirationRelativeToNow = null,
            CancellationToken cancellationToken = default)
            => factory(cancellationToken);
    }
}
