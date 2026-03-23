using IIoT.Edge.Common.Domain;
using IIoT.Edge.Common.Specification;
using System.Linq.Expressions;

namespace IIoT.Edge.Common.Repository;

public interface IReadRepository<T> where T : class, IAggregateRoot
{
    IQueryable<T> GetQueryable();

    Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
        where TKey : notnull;

    Task<T?> GetAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default);

    Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default);

    Task<List<T>> GetListAsync(
        Expression<Func<T, bool>> expression,
        Expression<Func<T, object>>[]? includes = null,
        CancellationToken cancellationToken = default);

    Task<List<T>> GetListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<T?> GetSingleOrDefaultAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<int> GetCountAsync(
        Expression<Func<T, bool>> expression,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);
}