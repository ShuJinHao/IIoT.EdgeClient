using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;

public sealed record CloudApiConfigParamDto(
    string Key,
    string Value,
    string? Description = null);

public sealed record SaveCloudApiConfigParamsCommand(
    List<CloudApiConfigParamDto> Params) : ICommand<Result>;

public sealed class SaveCloudApiConfigParamsHandler(
    IRepository<SystemConfigEntity> repo,
    IEdgeCacheService cache)
    : ICommandHandler<SaveCloudApiConfigParamsCommand, Result>
{
    public async Task<Result> Handle(
        SaveCloudApiConfigParamsCommand request,
        CancellationToken cancellationToken)
    {
        List<SystemConfigEntity> configs;
        try
        {
            configs = request.Params
                .GroupBy(x => x.Key?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last())
                .Select(dto =>
                {
                    var key = dto.Key?.Trim() ?? string.Empty;
                    var descriptor = CloudApiConfigParamSchema.Find(key)
                                     ?? throw new ArgumentException("云端配置键不在 CloudApi 白名单内。");
                    var entity = SystemConfigEntity.Create(
                        descriptor.Key,
                        dto.Value,
                        dto.Description);
                    entity.UpdateSortOrder(descriptor.SortOrder);
                    return entity;
                })
                .ToList();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }

        var keys = configs
            .Select(static x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count > 0)
        {
            await repo.ExecuteDeleteAsync(x => keys.Contains(x.Key), cancellationToken);
        }

        foreach (var config in configs)
        {
            repo.Add(config);
        }

        await repo.SaveChangesAsync(cancellationToken);
        cache.Remove(ParameterCacheKeys.SystemAll);
        return Result.Success();
    }
}
