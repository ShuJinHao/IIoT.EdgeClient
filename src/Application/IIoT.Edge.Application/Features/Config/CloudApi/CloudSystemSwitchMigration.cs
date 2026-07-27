using IIoT.Edge.Module.Contracts.Cache;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Application.Features.Config.CloudApi;

public interface ICloudSystemSwitchMigration
{
    Task<bool> MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 将旧插件级 Cloud 开关一次性收敛为当前 profile 的系统开关。
/// 迁移按系统开关 AND 全部旧插件开关计算，缺失、冲突或非法值均关闭。
/// </summary>
public sealed class CloudSystemSwitchMigration(
    IReadRepository<SystemConfigEntity> systemConfigs,
    IEdgeUnitOfWorkFactory unitOfWorkFactory,
    IEdgeCacheService cache,
    ICloudProfileSwitchProjectionWriter projectionWriter,
    ICloudApiConfigSnapshotProvider cloudApiConfigSnapshotProvider) : ICloudSystemSwitchMigration
{
    public const string MigrationMarkerKey = "EdgeMigration:CloudSystemSwitchV1";
    public const string BindingRepairMarkerKey = "EdgeMigration:CloudBindingSwitchRepairV2";
    private const string LegacyCloudSwitchSuffix = ":Cloud:启用";

    public async Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var configs = await systemConfigs
            .GetListAsync(static _ => true, cancellationToken)
            .ConfigureAwait(false);
        var cloudProfile = cloudApiConfigSnapshotProvider.GetCurrent();
        var hasCompleteBinding = HasCompleteBinding(cloudProfile);
        var completeBindingRequestsEnable = cloudProfile.Enabled && hasCompleteBinding;
        var shouldRepairBoundProfile = hasCompleteBinding
                                       && !configs.Any(static config =>
                                           string.Equals(
                                               config.Key,
                                               BindingRepairMarkerKey,
                                               StringComparison.OrdinalIgnoreCase));
        if (configs.Any(static config =>
                string.Equals(config.Key, MigrationMarkerKey, StringComparison.OrdinalIgnoreCase)))
        {
            var current = configs.FirstOrDefault(static config =>
                string.Equals(config.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase));
            var currentEnabled = TryReadEnabled(current?.Value);
            if ((completeBindingRequestsEnable || shouldRepairBoundProfile) && !currentEnabled)
            {
                await EnableFromCompleteBindingAsync(
                        markBindingRepairComplete: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                cache.Remove(ParameterCacheKeys.SystemAll);
                return true;
            }

            if (shouldRepairBoundProfile)
            {
                await ReplaceSystemSwitchAsync(
                        currentEnabled,
                        markBindingRepairComplete: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                cache.Remove(ParameterCacheKeys.SystemAll);
            }

            await projectionWriter.WriteAsync(currentEnabled, cancellationToken).ConfigureAwait(false);
            return true;
        }

        var systemSwitch = configs.FirstOrDefault(static config =>
            string.Equals(config.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase));
        var legacySwitches = configs
            .Where(static config =>
                config.Key.StartsWith("Module:", StringComparison.OrdinalIgnoreCase)
                && config.Key.EndsWith(LegacyCloudSwitchSuffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var enabled = completeBindingRequestsEnable
                      || shouldRepairBoundProfile
                      || (TryReadEnabled(systemSwitch?.Value)
                          && legacySwitches.Length > 0
                          && legacySwitches.All(static config => TryReadEnabled(config.Value)));

        if (enabled)
        {
            await ReplaceSystemSwitchAsync(
                    enabled,
                    markBindingRepairComplete: shouldRepairBoundProfile,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await ReplaceSystemSwitchAsync(
                        enabled: false,
                        markBindingRepairComplete: false,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await projectionWriter.WriteAsync(enabled: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        else
        {
            await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            await ReplaceSystemSwitchAsync(
                    enabled,
                    markBindingRepairComplete: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        cache.Remove(ParameterCacheKeys.SystemAll);
        return true;
    }

    private async Task EnableFromCompleteBindingAsync(
        bool markBindingRepairComplete,
        CancellationToken cancellationToken)
    {
        await ReplaceSystemSwitchAsync(
                enabled: true,
                markBindingRepairComplete,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await projectionWriter.WriteAsync(enabled: true, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await ReplaceSystemSwitchAsync(
                    enabled: false,
                    markBindingRepairComplete: false,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await projectionWriter.WriteAsync(enabled: false, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReplaceSystemSwitchAsync(
        bool enabled,
        bool markBindingRepairComplete,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var repository = unitOfWork.Repository<SystemConfigEntity>();
        var existing = await repository
            .GetListAsync(
                static config => config.Key == CloudApiConfigParamSchema.Enabled
                                 || config.Key == MigrationMarkerKey
                                 || config.Key == BindingRepairMarkerKey,
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var config in existing)
        {
            repository.Delete(config);
        }

        var systemEntity = SystemConfigEntity.Create(
            CloudApiConfigParamSchema.Enabled,
            enabled ? "true" : "false",
            "当前 machine profile 的 Cloud 通信总开关。");
        var descriptor = CloudApiConfigParamSchema.Find(CloudApiConfigParamSchema.Enabled);
        systemEntity.UpdateSortOrder(descriptor?.SortOrder ?? 0);
        repository.Add(systemEntity);
        repository.Add(CreateMigrationMarker());
        if (markBindingRepairComplete)
        {
            repository.Add(CreateBindingRepairMarker());
        }
        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SystemConfigEntity CreateMigrationMarker()
        => SystemConfigEntity.Create(
            MigrationMarkerKey,
            "1",
            "Cloud 系统唯一开关迁移标记。");

    private static SystemConfigEntity CreateBindingRepairMarker()
        => SystemConfigEntity.Create(
            BindingRepairMarkerKey,
            "1",
            "完整安装绑定的 Cloud 开关一次性修复标记。");

    private static bool TryReadEnabled(string? value)
        => bool.TryParse(value?.Trim(), out var enabled) && enabled;

    private static bool HasCompleteBinding(CloudApiConfigSnapshot snapshot)
        => !string.IsNullOrWhiteSpace(snapshot.ClientCode)
           && !string.IsNullOrWhiteSpace(snapshot.BootstrapSecret)
           && Uri.TryCreate(snapshot.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
           && (baseUri.Scheme == Uri.UriSchemeHttp || baseUri.Scheme == Uri.UriSchemeHttps)
           && !string.IsNullOrWhiteSpace(baseUri.Host);
}
