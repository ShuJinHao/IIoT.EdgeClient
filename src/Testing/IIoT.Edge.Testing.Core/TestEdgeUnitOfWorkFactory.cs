using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Testing;

/// <summary>
/// Keeps repository-focused tests explicit about their transaction boundary without
/// requiring an EF Core context. Repositories are registered by their closed
/// <see cref="IRepository{T}"/> contract and reused by each short-lived test unit of work.
/// </summary>
public sealed class TestEdgeUnitOfWorkFactory : IEdgeUnitOfWorkFactory
{
    private readonly IReadOnlyDictionary<Type, object> _repositories;

    public TestEdgeUnitOfWorkFactory(params object[] repositories)
    {
        var registrations = new Dictionary<Type, object>();
        foreach (var repository in repositories)
        {
            var contracts = repository.GetType().GetInterfaces()
                .Where(static contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IRepository<>))
                .ToArray();

            if (contracts.Length != 1)
            {
                throw new ArgumentException(
                    $"Test repository {repository.GetType().FullName} must implement exactly one IRepository<T> contract.",
                    nameof(repositories));
            }

            registrations[contracts[0].GenericTypeArguments[0]] = repository;
        }

        _repositories = registrations;
    }

    public int BeginCount { get; private set; }

    public int CommitCount { get; private set; }

    public Task<IEdgeUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeginCount++;
        return Task.FromResult<IEdgeUnitOfWork>(new TestEdgeUnitOfWork(this, _repositories));
    }

    private sealed class TestEdgeUnitOfWork(
        TestEdgeUnitOfWorkFactory owner,
        IReadOnlyDictionary<Type, object> repositories) : IEdgeUnitOfWork
    {
        private bool _committed;
        private bool _disposed;

        public IRepository<T> Repository<T>()
            where T : class, IEntity, IAggregateRoot
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return repositories.TryGetValue(typeof(T), out var repository)
                ? (IRepository<T>)repository
                : throw new InvalidOperationException($"No test repository was registered for {typeof(T).FullName}.");
        }

        public Task<int> FlushAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_committed)
            {
                throw new InvalidOperationException("The test unit of work has already been committed.");
            }

            _committed = true;
            owner.CommitCount++;
            return Task.FromResult(0);
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
