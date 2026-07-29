using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Common.Crud;

internal static class SubmittedEntityListSaveHelper
{
    public static async Task<Result> ExecuteInUnitOfWorkAsync<TEntity>(
        IEdgeUnitOfWorkFactory unitOfWorkFactory,
        Func<IRepository<TEntity>, CancellationToken, Task<Result>> applyAsync,
        CancellationToken cancellationToken)
        where TEntity : class, IEntity, IAggregateRoot
    {
        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = await applyAsync(
                unitOfWork.Repository<TEntity>(),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result;
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static Task<Result> ReplaceSubmittedAsync<TEntity, TDto>(
        IRepository<TEntity> repo,
        IReadOnlyCollection<TDto> submittedItems,
        Func<CancellationToken, Task<List<TEntity>>> loadExistingAsync,
        Func<TDto, int> getSubmittedId,
        Func<TDto, string?> validate,
        Func<TDto, TEntity> create,
        Action<TEntity, TDto> apply,
        CancellationToken cancellationToken)
        where TEntity : class, IEntity<int>, IAggregateRoot
        => ReplaceSubmittedAsync(
            repo,
            submittedItems,
            loadExistingAsync,
            getSubmittedId,
            validate,
            create,
            apply,
            static _ => true,
            static (_, _) => true,
            cancellationToken);

    public static async Task<Result> ReplaceSubmittedAsync<TEntity, TDto>(
        IRepository<TEntity> repo,
        IReadOnlyCollection<TDto> submittedItems,
        Func<CancellationToken, Task<List<TEntity>>> loadExistingAsync,
        Func<TDto, int> getSubmittedId,
        Func<TDto, string?> validate,
        Func<TDto, TEntity> create,
        Action<TEntity, TDto> apply,
        Func<TEntity, bool> shouldDelete,
        Func<TEntity, TDto, bool> shouldUpdate,
        CancellationToken cancellationToken)
        where TEntity : class, IEntity<int>, IAggregateRoot
    {
        foreach (var item in submittedItems)
        {
            var validationError = validate(item);
            if (validationError is not null)
            {
                return Result.Failure(validationError);
            }
        }

        var existingItems = await loadExistingAsync(cancellationToken).ConfigureAwait(false);
        var existingById = existingItems.ToDictionary(static x => x.Id);
        var submittedIds = submittedItems
            .Select(getSubmittedId)
            .Where(static id => id > 0)
            .ToHashSet();

        foreach (var entity in existingItems.Where(
                     x => !submittedIds.Contains(x.Id) && shouldDelete(x)))
        {
            repo.Delete(entity);
        }

        foreach (var item in submittedItems)
        {
            try
            {
                var itemId = getSubmittedId(item);
                if (itemId == 0)
                {
                    var entity = create(item);
                    apply(entity, item);
                    repo.Add(entity);
                }
                else if (existingById.TryGetValue(itemId, out var entity)
                         && shouldUpdate(entity, item))
                {
                    apply(entity, item);
                    repo.Update(entity);
                }
            }
            catch (ArgumentException ex)
            {
                return Result.Failure(ex.Message);
            }
        }

        return Result.Success();
    }
}
