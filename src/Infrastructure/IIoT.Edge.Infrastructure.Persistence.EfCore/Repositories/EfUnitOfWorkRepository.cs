using IIoT.Edge.Infrastructure.Persistence.EfCore.Specifications;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;

internal sealed class EfUnitOfWorkRepository<T>(EdgeDbContext db, Action ensureActive) : IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    public T Add(T entity)
    {
        ensureActive();
        db.Set<T>().Add(entity);
        return entity;
    }

    public void Update(T entity)
    {
        ensureActive();
        var keyValues = ResolvePrimaryKeyValues(entity);
        var persisted = db.Set<T>().Find(keyValues)
            ?? throw new InvalidOperationException($"无法更新不存在的实体 {typeof(T).Name}。");

        db.Entry(persisted).CurrentValues.SetValues(entity);
    }

    public void Delete(T entity)
    {
        ensureActive();
        db.Set<T>().Remove(entity);
    }

    public async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
        => await db.Set<T>().FindAsync([id], cancellationToken).ConfigureAwait(false);

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyIncludes(db.Set<T>(), includes);
        return await query.FirstOrDefaultAsync(expression, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
        => await db.Set<T>().Where(expression).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
        => await ApplyIncludes(db.Set<T>(), includes)
            .Where(expression)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
        => await db.Set<T>().CountAsync(expression, cancellationToken).ConfigureAwait(false);

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);

    private static IQueryable<T> ApplyIncludes(
        IQueryable<T> query,
        IEnumerable<Expression<Func<T, object>>>? includes)
    {
        if (includes is null)
        {
            return query;
        }

        foreach (var include in includes)
        {
            query = query.Include(include);
        }

        return query;
    }

    private object[] ResolvePrimaryKeyValues(T entity)
    {
        var entityType = db.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"实体 {typeof(T).Name} 未注册到 EF 模型。");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"实体 {typeof(T).Name} 未配置主键。");

        return primaryKey.Properties
            .Select(property =>
            {
                var propertyInfo = typeof(T).GetProperty(property.Name)
                    ?? throw new InvalidOperationException($"实体 {typeof(T).Name} 缺少主键属性 {property.Name}。");
                return propertyInfo.GetValue(entity)
                    ?? throw new InvalidOperationException($"实体 {typeof(T).Name} 的主键 {property.Name} 不能为空。");
            })
            .ToArray();
    }
}
