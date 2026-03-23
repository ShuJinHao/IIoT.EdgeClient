using IIoT.Edge.Common.Domain;
using IIoT.Edge.Common.Repository;
using IIoT.Edge.Common.Specification;
using IIoT.Edge.Infrastructure.Specification;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IIoT.Edge.Infrastructure.Repositories;

public class EfReadRepository<T>(EdgeDbContext dbContext) : IReadRepository<T>
    where T : class, IAggregateRoot
{
    public IQueryable<T> GetQueryable()
        => dbContext.Set<T>().AsQueryable();

    public async Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull
        => await dbContext.Set<T>().FindAsync([id], cancellationToken);

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<T>().AsQueryable();
        if (includes != null)
            foreach (var include in includes)
                query = query.Include(include);

        return await query.FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
        => await dbContext.Set<T>().Where(expression).ToListAsync(cancellationToken);

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Set<T>().AsQueryable();
        if (includes != null)
            foreach (var include in includes)
                query = query.Include(include);

        return await query.Where(expression).ToListAsync(cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken);

    public async Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
        => await dbContext.Set<T>().Where(expression).CountAsync(cancellationToken);

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .CountAsync(cancellationToken);

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
        => await SpecificationEvaluator
            .GetQuery(dbContext.Set<T>().AsQueryable(), specification)
            .AnyAsync(cancellationToken);
}