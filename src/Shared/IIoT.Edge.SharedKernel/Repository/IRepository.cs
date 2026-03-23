using IIoT.Edge.Common.Domain;

namespace IIoT.Edge.Common.Repository;

public interface IRepository<T> : IReadRepository<T> where T : class, IEntity, IAggregateRoot
{
    T Add(T entity);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}