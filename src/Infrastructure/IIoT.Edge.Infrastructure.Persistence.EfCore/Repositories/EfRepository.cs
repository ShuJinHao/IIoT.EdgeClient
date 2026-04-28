using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;

public class EfRepository<T>(
    IDbContextFactory<EdgeDbContext> factory)
    : EfReadRepository<T>(factory), IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    // 用 pending ops 替换共享 _currentDb：
    // Add/Update/Delete 把操作入队，SaveChangesAsync 创建一次性 DbContext 统一执行。
    // 彻底消除 Singleton + 共享 DbContext 状态，不再需要 lock。
    private readonly List<Action<EdgeDbContext>> _pendingOps = [];
    private readonly object _lock = new();

    public T Add(T entity)
    {
        lock (_lock) { _pendingOps.Add(db => db.Set<T>().Add(entity)); }
        return entity;
    }

    public void Update(T entity)
    {
        lock (_lock) { _pendingOps.Add(db => db.Set<T>().Update(entity)); }
    }

    public void Delete(T entity)
    {
        lock (_lock) { _pendingOps.Add(db => db.Set<T>().Remove(entity)); }
    }

    public async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        List<Action<EdgeDbContext>> ops;
        lock (_lock)
        {
            if (_pendingOps.Count == 0) return 0;
            ops = [.._pendingOps];
            _pendingOps.Clear();
        }

        using var db = _factory.CreateDbContext();
        foreach (var op in ops)
            op(db);

        return await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ExecuteDeleteAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // 独立 DbContext，一条 SQL 批量删除，不经过 ChangeTracker
        using var db = _factory.CreateDbContext();
        return await db.Set<T>()
            .Where(predicate)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
