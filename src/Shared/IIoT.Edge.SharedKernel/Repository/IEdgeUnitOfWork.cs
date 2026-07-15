using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.SharedKernel.Repository;

public interface IEdgeUnitOfWorkFactory
{
    Task<IEdgeUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}

public interface IEdgeUnitOfWork : IAsyncDisposable
{
    IRepository<T> Repository<T>()
        where T : class, IEntity, IAggregateRoot;

    Task<int> FlushAsync(CancellationToken cancellationToken = default);

    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
