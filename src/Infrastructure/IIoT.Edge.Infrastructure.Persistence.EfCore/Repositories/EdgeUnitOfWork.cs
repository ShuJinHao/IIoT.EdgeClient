using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;

internal sealed class EdgeUnitOfWorkFactory(IDbContextFactory<EdgeDbContext> factory) : IEdgeUnitOfWorkFactory
{
    public async Task<IEdgeUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            return new EdgeUnitOfWork(db, transaction);
        }
        catch
        {
            await db.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

internal sealed class EdgeUnitOfWork(
    EdgeDbContext db,
    IDbContextTransaction transaction) : IEdgeUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];
    private UnitOfWorkState _state;

    public IRepository<T> Repository<T>()
        where T : class, IEntity, IAggregateRoot
    {
        EnsureActive();
        if (_repositories.TryGetValue(typeof(T), out var existing))
        {
            return (IRepository<T>)existing;
        }

        var repository = new EfUnitOfWorkRepository<T>(db, EnsureActive);
        _repositories.Add(typeof(T), repository);
        return repository;
    }

    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException primaryException) when (cancellationToken.IsCancellationRequested)
        {
            await FaultAndRollbackAsync(primaryException).ConfigureAwait(false);
            RethrowCallerCancellation(primaryException, cancellationToken);
            throw;
        }
        catch (Exception primaryException)
        {
            await FaultAndRollbackAsync(primaryException).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<int> CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var affected = await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _state = UnitOfWorkState.Committed;
            return affected;
        }
        catch (OperationCanceledException primaryException) when (cancellationToken.IsCancellationRequested)
        {
            await FaultAndRollbackAsync(primaryException).ConfigureAwait(false);
            RethrowCallerCancellation(primaryException, cancellationToken);
            throw;
        }
        catch (Exception primaryException)
        {
            await FaultAndRollbackAsync(primaryException).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_state == UnitOfWorkState.Active)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _state = UnitOfWorkState.Disposed;
            await transaction.DisposeAsync().ConfigureAwait(false);
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EnsureActive()
    {
        if (_state != UnitOfWorkState.Active)
        {
            throw new InvalidOperationException($"工作单元当前状态为 {_state}，不能继续写入或提交。");
        }
    }

    private async Task FaultAndRollbackAsync(Exception primaryException)
    {
        _state = UnitOfWorkState.Faulted;
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception rollbackException)
        {
            primaryException.Data["IIoT.Edge.Persistence.RollbackException"] = rollbackException;
        }
    }

    private static void RethrowCallerCancellation(
        OperationCanceledException primaryException,
        CancellationToken cancellationToken)
    {
        if (primaryException.CancellationToken == cancellationToken)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(primaryException)
                .Throw();
        }

        var normalized = new OperationCanceledException(
            primaryException.Message,
            primaryException,
            cancellationToken);
        foreach (System.Collections.DictionaryEntry entry in primaryException.Data)
        {
            normalized.Data[entry.Key] = entry.Value;
        }

        throw normalized;
    }

    private enum UnitOfWorkState
    {
        Active,
        Committed,
        Faulted,
        Disposed
    }
}
