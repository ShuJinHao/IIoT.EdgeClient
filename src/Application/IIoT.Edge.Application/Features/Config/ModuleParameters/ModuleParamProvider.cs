using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Features.Config;

namespace IIoT.Edge.Application.Features.Config.ModuleParameters;

/// <summary>
/// 插件运行时参数读取器，复用客户端通用缓存服务生成模块参数快照。
/// </summary>
public sealed class ModuleParamProvider<TMes, TCloud, TBusiness>(
    IModuleParamRegistry registry,
    ILocalParameterConfigService localParameterConfigService,
    IEdgeCacheService cache,
    ILogService logger)
    : IModuleParamProvider<TMes, TCloud, TBusiness>
    where TMes : struct, Enum
    where TCloud : struct, Enum
    where TBusiness : struct, Enum
{
    public async Task<ModuleParamSnapshot<TMes, TCloud, TBusiness>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!registry.TryGetRegistration(typeof(TMes), typeof(TCloud), typeof(TBusiness), out var registration))
        {
            throw new InvalidOperationException(
                $"插件参数枚举未注册：{typeof(TMes).Name}/{typeof(TCloud).Name}/{typeof(TBusiness).Name}。");
        }

        var valueSnapshot = await cache.GetOrCreateAsync(
                ParameterCacheKeys.ModuleSnapshot(registration.ModuleId),
                ct => LoadValueSnapshotAsync(registration.ModuleId, ct),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (valueSnapshot is null)
        {
            throw new InvalidOperationException($"插件参数快照加载失败：{registration.ModuleId}。");
        }

        return new ModuleParamSnapshot<TMes, TCloud, TBusiness>(
            registration.ModuleId,
            CreateGroup<TMes>(registration.ModuleId, ModuleParamCategory.Mes, valueSnapshot.Values),
            CreateGroup<TCloud>(registration.ModuleId, ModuleParamCategory.Cloud, valueSnapshot.Values),
            CreateGroup<TBusiness>(registration.ModuleId, ModuleParamCategory.Business, valueSnapshot.Values));
    }

    private async Task<ModuleParamValueSnapshot?> LoadValueSnapshotAsync(
        string moduleId,
        CancellationToken cancellationToken)
    {
        var prefix = $"{ModuleParamKeys.StoragePrefix}{moduleId}:";
        var configuredValues = (await localParameterConfigService
                .GetSystemConfigsAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(x => x.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static x => x.Key,
                static x => x.Value,
                StringComparer.OrdinalIgnoreCase);

        return new ModuleParamValueSnapshot(moduleId, configuredValues);
    }

    private ModuleParamGroup<TEnum> CreateGroup<TEnum>(
        string moduleId,
        ModuleParamCategory category,
        IReadOnlyDictionary<string, string> configuredValues)
        where TEnum : struct, Enum
    {
        var values = new Dictionary<TEnum, string>();
        var defaults = new Dictionary<TEnum, string?>();
        var valueKinds = new Dictionary<TEnum, ParamValueKind>();
        foreach (var descriptor in registry.GetDescriptors(moduleId, category))
        {
            if (descriptor.EnumType != typeof(TEnum)
                || !Enum.TryParse<TEnum>(descriptor.Name, out var enumValue))
            {
                continue;
            }

            defaults[enumValue] = descriptor.DefaultValue;
            valueKinds[enumValue] = descriptor.ValueKind;
            if (configuredValues.TryGetValue(descriptor.StorageKey, out var configured))
            {
                values[enumValue] = configured;
            }
        }

        return new ModuleParamGroup<TEnum>(
            moduleId,
            category,
            values,
            defaults,
            valueKinds,
            logger.Warn);
    }
}
