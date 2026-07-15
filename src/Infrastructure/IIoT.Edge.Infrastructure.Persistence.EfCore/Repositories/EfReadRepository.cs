using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Specifications;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;

public class EfReadRepository<T>(
    IDbContextFactory<EdgeDbContext> factory)
    : IReadRepository<T>
    where T : class, IAggregateRoot
{
    protected readonly IDbContextFactory<EdgeDbContext>
        _factory = factory;

    public async Task<T?> GetByIdAsync<TKey>(
        TKey id,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        using var db = _factory.CreateDbContext();
        return await db.Set<T>()
            .FindAsync([id], cancellationToken);
    }

    public async Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        var query = db.Set<T>().AsQueryable();
        if (includes != null)
            foreach (var include in includes)
                query = query.Include(include);

        return await query
            .FirstOrDefaultAsync(expression, cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.Set<T>()
            .Where(expression)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        var query = db.Set<T>().AsQueryable();
        if (includes != null)
            foreach (var include in includes)
                query = query.Include(include);

        return await query
            .Where(expression)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .ToListAsync(cancellationToken);
    }

    public async Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await db.Set<T>()
            .Where(expression)
            .CountAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        using var db = _factory.CreateDbContext();
        return await SpecificationEvaluator
            .GetQuery(db.Set<T>().AsQueryable(), specification)
            .AnyAsync(cancellationToken);
    }
}
