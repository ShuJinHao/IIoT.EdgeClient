using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
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
    IRepository<SystemConfigEntity> systemConfigs,
    IEdgeCacheService cache,
    ICloudProfileSwitchProjectionWriter projectionWriter) : ICloudSystemSwitchMigration
{
    public const string MigrationMarkerKey = "EdgeMigration:CloudSystemSwitchV1";
    private const string LegacyCloudSwitchSuffix = ":Cloud:启用";

    public async Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var configs = await systemConfigs
            .GetListAsync(static _ => true, cancellationToken)
            .ConfigureAwait(false);
        if (configs.Any(static config =>
                string.Equals(config.Key, MigrationMarkerKey, StringComparison.OrdinalIgnoreCase)))
        {
            var current = configs.FirstOrDefault(static config =>
                string.Equals(config.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase));
            await projectionWriter
                .WriteAsync(TryReadEnabled(current?.Value), cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        var systemSwitch = configs.FirstOrDefault(static config =>
            string.Equals(config.Key, CloudApiConfigParamSchema.Enabled, StringComparison.OrdinalIgnoreCase));
        var legacySwitches = configs
            .Where(static config =>
                config.Key.StartsWith("Module:", StringComparison.OrdinalIgnoreCase)
                && config.Key.EndsWith(LegacyCloudSwitchSuffix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var enabled = TryReadEnabled(systemSwitch?.Value)
                      && legacySwitches.Length > 0
                      && legacySwitches.All(static config => TryReadEnabled(config.Value));

        if (enabled)
        {
            await ReplaceSystemSwitchAsync(enabled, includeMarker: false, cancellationToken).ConfigureAwait(false);
            await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            systemConfigs.Add(CreateMigrationMarker());
            await systemConfigs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            await ReplaceSystemSwitchAsync(enabled, includeMarker: true, cancellationToken).ConfigureAwait(false);
        }

        cache.Remove(ParameterCacheKeys.SystemAll);
        return true;
    }

    private async Task ReplaceSystemSwitchAsync(
        bool enabled,
        bool includeMarker,
        CancellationToken cancellationToken)
    {
        await systemConfigs.ExecuteDeleteAsync(
            static config => config.Key == CloudApiConfigParamSchema.Enabled
                             || config.Key == MigrationMarkerKey,
            cancellationToken).ConfigureAwait(false);

        var systemEntity = SystemConfigEntity.Create(
            CloudApiConfigParamSchema.Enabled,
            enabled ? "true" : "false",
            "当前 machine profile 的 Cloud 通信总开关。");
        var descriptor = CloudApiConfigParamSchema.Find(CloudApiConfigParamSchema.Enabled);
        systemEntity.UpdateSortOrder(descriptor?.SortOrder ?? 0);
        systemConfigs.Add(systemEntity);
        if (includeMarker)
        {
            systemConfigs.Add(CreateMigrationMarker());
        }

        await systemConfigs.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SystemConfigEntity CreateMigrationMarker()
        => SystemConfigEntity.Create(
            MigrationMarkerKey,
            "1",
            "Cloud 系统唯一开关迁移标记。");

    private static bool TryReadEnabled(string? value)
        => bool.TryParse(value?.Trim(), out var enabled) && enabled;
}
