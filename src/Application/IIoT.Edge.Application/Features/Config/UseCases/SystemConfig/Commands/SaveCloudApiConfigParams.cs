using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
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
    IEdgeCacheService cache,
    ILocalParameterConfigChangePublisher changePublisher,
    ILocalSystemRuntimeConfigService runtimeConfig,
    ICloudProfileSwitchProjectionWriter projectionWriter)
    : ICommandHandler<SaveCloudApiConfigParamsCommand, Result>
{
    public async Task<Result> Handle(
        SaveCloudApiConfigParamsCommand request,
        CancellationToken cancellationToken)
    {
        bool? requestedCloudEnabled = null;
        var enabledParam = request.Params.FirstOrDefault(static parameter =>
            string.Equals(parameter.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase));
        if (enabledParam is not null)
        {
            if (!bool.TryParse(enabledParam.Value?.Trim(), out var parsedEnabled))
            {
                return Result.Failure("Cloud 系统开关必须为 true 或 false。");
            }

            requestedCloudEnabled = parsedEnabled;
        }

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

        if (requestedCloudEnabled == false
            && !await TryWriteProjectionAsync(false, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure("Cloud 系统开关投影写入失败，已保持原配置。");
        }

        await SystemConfigParamSaveHelper.ReplaceByKeysAsync(
            repo,
            configsResult.Value ?? [],
            cancellationToken);

        cache.Remove(ParameterCacheKeys.SystemAll);
        await runtimeConfig.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (requestedCloudEnabled == true
            && !await TryWriteProjectionAsync(true, cancellationToken).ConfigureAwait(false))
        {
            await RollBackCloudEnableAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure("Cloud 系统开关投影写入失败，已回滚为关闭。");
        }

        changePublisher.NotifyModuleChanged();
        return Result.Success();
    }

    private async Task<bool> TryWriteProjectionAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task RollBackCloudEnableAsync(CancellationToken cancellationToken)
    {
        var descriptor = CloudApiConfigParamSchema.Find(CloudApiConfigParamSchema.Enabled)
                         ?? throw new InvalidOperationException("Cloud 系统开关描述缺失。");
        var disabled = SystemConfigEntity.Create(
            descriptor.Key,
            "false",
            "Cloud 系统开关投影写入失败，已自动回滚。");
        disabled.UpdateSortOrder(descriptor.SortOrder);
        await SystemConfigParamSaveHelper.ReplaceByKeysAsync(
            repo,
            [disabled],
            cancellationToken).ConfigureAwait(false);
        cache.Remove(ParameterCacheKeys.SystemAll);
        await runtimeConfig.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
