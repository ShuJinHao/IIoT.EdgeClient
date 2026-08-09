using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;

/// <summary>
/// 单条插件参数的数据传输对象。
/// </summary>
public sealed record ModuleParamDto(
    string Key,
    string Value,
    string? Description = null);

/// <summary>
/// 保存插件枚举参数，只覆盖传入 key 对应的模块参数。
/// </summary>
public sealed record SaveModuleParamsCommand(
    List<ModuleParamDto> Params) : ICommand<Result>;

public sealed class SaveModuleParamsHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores,
    ILocalParameterConfigChangePublisher changePublisher)
    : ICommandHandler<SaveModuleParamsCommand, Result>
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();

    public async Task<Result> Handle(
        SaveModuleParamsCommand request,
        CancellationToken cancellationToken)
    {
        if (_stores.Length != 1)
        {
            return Result.Failure("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var submitted = new Dictionary<string, ModuleParamDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in request.Params ?? [])
        {
            var key = parameter.Key?.Trim() ?? string.Empty;
            if (!ModuleParamKeys.IsModuleStorageKey(key)
                || parameter.Value is null
                || !submitted.TryAdd(key, parameter with { Key = key }))
            {
                return Result.Failure("PLUGIN_MODULE_SETTINGS_INVALID");
            }
        }

        if (submitted.Count == 0)
        {
            return Result.Success();
        }

        var snapshot = snapshots.GetRequiredSnapshot();
        var existing = snapshot.ModuleSettings.ToDictionary(
            static item => item.Key,
            StringComparer.OrdinalIgnoreCase);
        var next = snapshot.ModuleSettings
            .Where(item => !submitted.ContainsKey(item.Key))
            .ToList();
        var nextSortOrder = next.Count == 0 ? 1 : next.Max(static item => item.SortOrder) + 1;
        foreach (var parameter in submitted.Values)
        {
            existing.TryGetValue(parameter.Key, out var current);
            next.Add(new DevicePluginModuleSetting(
                parameter.Key,
                parameter.Value,
                parameter.Description ?? current?.DisplayName,
                current?.Unit,
                current?.SortOrder ?? nextSortOrder++));
        }

        var result = await _stores[0]
            .UpdateModuleSettingsAsync(
                next.OrderBy(static item => item.SortOrder).ToArray(),
                snapshot.ConfigurationVersion,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Result.Failure(
                result.FailureReasonCode ?? "PLUGIN_MODULE_SETTINGS_WRITE_REJECTED");
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        changePublisher.NotifyModuleChanged();
        return Result.Success();
    }
}
