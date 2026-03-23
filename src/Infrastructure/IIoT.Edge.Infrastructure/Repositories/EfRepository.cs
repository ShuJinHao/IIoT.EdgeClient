using IIoT.Edge.Common.Domain;
using IIoT.Edge.Common.Repository;
using Microsoft.EntityFrameworkCore.Migrations;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace IIoT.Edge.Infrastructure.Repositories;

public class EfRepository<T>(EdgeDbContext dbContext) : EfReadRepository<T>(dbContext), IRepository<T>
    where T : class, IEntity, IAggregateRoot
{
    private readonly EdgeDbContext _dbContext = dbContext;

    public T Add(T entity)
    {
        _dbContext.Set<T>().Add(entity);
        return entity;
    }

    public void Update(T entity)
        => _dbContext.Set<T>().Update(entity);

    public void Delete(T entity)
        => _dbContext.Set<T>().Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _dbContext.SaveChangesAsync(cancellationToken);
}