using IIoT.Edge.SharedKernel.Domain;
using System.Linq.Expressions;

namespace IIoT.Edge.SharedKernel.Repository;

/// <summary>
/// 可写仓储契约。
/// 在只读仓储基础上补充增删改与持久化能力。
/// </summary>
public interface IRepository<T> : IReadRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    T Add(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量删除。
    /// 通过单条 SQL 执行，不经过 ChangeTracker。
    /// </summary>
    Task<int> ExecuteDeleteAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);
}
