using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Common.Crud;

internal static class SubmittedEntityListSaveHelper
{
    public static async Task<Result> ReplaceSubmittedAsync<TEntity, TDto>(
        IRepository<TEntity> repo,
        IReadOnlyCollection<TDto> submittedItems,
        Func<CancellationToken, Task<List<TEntity>>> loadExistingAsync,
        Func<TDto, int> getSubmittedId,
        Func<TDto, string?> validate,
        Func<TDto, TEntity> create,
        Action<TEntity, TDto> apply,
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

        foreach (var entity in existingItems.Where(x => !submittedIds.Contains(x.Id)))
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
                else if (existingById.TryGetValue(itemId, out var entity))
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

        await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
