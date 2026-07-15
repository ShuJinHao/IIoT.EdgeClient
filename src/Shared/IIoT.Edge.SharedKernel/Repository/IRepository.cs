using IIoT.Edge.SharedKernel.Domain;
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
}
