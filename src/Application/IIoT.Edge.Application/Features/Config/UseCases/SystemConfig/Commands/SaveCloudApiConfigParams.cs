using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Common.Config;
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
        var configsResult = SystemConfigParamSaveHelper.BuildDistinctConfigs(
            request.Params,
            static dto => dto.Key,
            static (dto, key, _) =>
            {
                var descriptor = CloudApiConfigParamSchema.Find(key)
                                 ?? throw new ArgumentException("云端配置键不在 CloudApi 白名单内。");
                var entity = SystemConfigEntity.Create(
                    descriptor.Key,
                    dto.Value,
                    dto.Description);
                entity.UpdateSortOrder(descriptor.SortOrder);
                return entity;
            });
        if (!configsResult.IsSuccess)
        {
            return Result.Failure(configsResult.ErrorMessage ?? "云端配置保存失败。");
        }

        await SystemConfigParamSaveHelper.ReplaceByKeysAsync(
            repo,
            configsResult.Value ?? [],
            cancellationToken);

        cache.Remove(ParameterCacheKeys.SystemAll);
        return Result.Success();
    }
}
