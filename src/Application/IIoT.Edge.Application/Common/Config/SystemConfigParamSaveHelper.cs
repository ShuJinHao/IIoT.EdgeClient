using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Common.Config;

public static class SystemConfigParamSaveHelper
{
    public static Result<List<SystemConfigEntity>> BuildDistinctConfigs<TParam>(
        IEnumerable<TParam> submittedParams,
        Func<TParam, string?> getKey,
        Func<TParam, string, int, SystemConfigEntity> createEntity)
    {
        try
        {
            var configs = submittedParams
                .GroupBy(param => NormalizeKey(getKey(param)), StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last())
                .Select((param, index) => createEntity(param, NormalizeKey(getKey(param)), index))
                .ToList();

            return Result.Success(configs);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public static async Task ReplaceByKeysAsync(
        IRepository<SystemConfigEntity> repo,
        IReadOnlyCollection<SystemConfigEntity> configs,
        CancellationToken cancellationToken)
    {
        var keys = configs
            .Select(static config => config.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count > 0)
        {
            await repo.ExecuteDeleteAsync(config => keys.Contains(config.Key), cancellationToken);
        }

        foreach (var config in configs)
        {
            repo.Add(config);
        }

        await repo.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeKey(string? key)
        => key?.Trim() ?? string.Empty;
}
