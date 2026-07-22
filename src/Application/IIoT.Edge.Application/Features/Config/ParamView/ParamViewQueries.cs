using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using MediatR;

namespace IIoT.Edge.Application.Features.Config.ParamView;

public record ModuleParamSnapshot(
    string ModuleId,
    ModuleParamCategory Category,
    string Key,
    string Name,
    string DisplayNameResourceKey,
    string DisplayNameFallback,
    string DescriptionResourceKey,
    string DescriptionFallback,
    ParamValueKind ValueKind,
    string Value,
    string DefaultValue,
    string Unit,
    string Min,
    string Max);

public record ModuleParamGroupSnapshot(
    string ModuleId,
    string ModuleDisplayName,
    List<ModuleParamSnapshot> Params,
    string ModuleDisplayNameResourceKey = "",
    string ModuleDisplayNameFallback = "");

public record ParamViewInitResult(
    List<ModuleParamGroupSnapshot> MesParamGroups,
    List<ModuleParamGroupSnapshot> CloudParamGroups,
    List<ModuleParamGroupSnapshot> BusinessParamGroups);

public record LoadParamViewQuery : IRequest<ParamViewInitResult>;

public sealed record ParamViewValueDto(
    string Key,
    string Value,
    string? Description = null);

public record SaveParamViewCommand(List<ParamViewValueDto> Params) : IRequest<CrudOperationResult>;

public record ResetParamViewCommand : IRequest<CrudOperationResult>;

public class LoadParamViewHandler(
    ILocalParameterConfigService localParameterConfigService,
    IModuleParamRegistry moduleParamRegistry,
    IEnumerable<IEdgeProcessModule> modules,
    ICloudApiConfigSnapshotProvider cloudApiConfigSnapshotProvider)
    : IRequestHandler<LoadParamViewQuery, ParamViewInitResult>
{
    public async Task<ParamViewInitResult> Handle(LoadParamViewQuery request, CancellationToken ct)
    {
        var systemSnapshots = await localParameterConfigService.GetSystemConfigsAsync(ct);
        var moduleValues = systemSnapshots
            .Where(snapshot => ModuleParamKeys.IsModuleStorageKey(snapshot.Key))
            .ToDictionary(
                static x => x.Key,
                static x => x.Value,
                StringComparer.OrdinalIgnoreCase);
        var moduleNames = modules.ToDictionary(
            static x => x.ModuleId,
            static x => x.DisplayName,
            StringComparer.OrdinalIgnoreCase);

        return new ParamViewInitResult(
            BuildModuleGroups(ModuleParamCategory.Mes, moduleParamRegistry, moduleNames, moduleValues),
            BuildCloudGroups(moduleParamRegistry, moduleNames, moduleValues, systemSnapshots, cloudApiConfigSnapshotProvider.GetCurrent()),
            BuildModuleGroups(ModuleParamCategory.Business, moduleParamRegistry, moduleNames, moduleValues));
    }

    private static List<ModuleParamGroupSnapshot> BuildCloudGroups(
        IModuleParamRegistry moduleParamRegistry,
        IReadOnlyDictionary<string, string> moduleNames,
        IReadOnlyDictionary<string, string> moduleValues,
        IReadOnlyCollection<LocalSystemConfigSnapshot> systemSnapshots,
        CloudApiConfigSnapshot cloudApiSnapshot)
    {
        var parameters = BuildCloudApiGroup(systemSnapshots, cloudApiSnapshot).Params
            .Concat(BuildModuleGroups(ModuleParamCategory.Cloud, moduleParamRegistry, moduleNames, moduleValues)
                .SelectMany(static group => group.Params))
            .ToList();

        return
        [
            new ModuleParamGroupSnapshot(
                CloudApiConfigParamSchema.ModuleId,
                CloudApiConfigParamSchema.GroupDisplayNameFallback,
                parameters,
                "Navigation_Tab_CloudParams",
                "云端参数")
        ];
    }

    private static ModuleParamGroupSnapshot BuildCloudApiGroup(
        IReadOnlyCollection<LocalSystemConfigSnapshot> systemSnapshots,
        CloudApiConfigSnapshot cloudApiSnapshot)
    {
        var values = systemSnapshots
            .Where(static snapshot => CloudApiConfigParamSchema.IsCloudApiConfigKey(snapshot.Key))
            .ToDictionary(
                static snapshot => snapshot.Key,
                static snapshot => snapshot.Value,
                StringComparer.OrdinalIgnoreCase);
        var parameters = CloudApiConfigParamSchema.Descriptors
            .Where(static descriptor => CloudApiConfigParamSchema.IsParamViewEditableKey(descriptor.Key))
            .OrderBy(static descriptor => descriptor.SortOrder)
            .Select(descriptor => new ModuleParamSnapshot(
                CloudApiConfigParamSchema.ModuleId,
                ModuleParamCategory.Cloud,
                descriptor.Key,
                descriptor.Name,
                descriptor.DisplayNameResourceKey,
                descriptor.DisplayNameFallback,
                descriptor.DescriptionResourceKey,
                descriptor.DescriptionFallback,
                descriptor.ValueKind,
                values.TryGetValue(descriptor.Key, out var configured)
                    ? configured
                    : CloudApiConfigParamSchema.GetDefaultValue(descriptor.Key, cloudApiSnapshot),
                CloudApiConfigParamSchema.GetDefaultValue(descriptor.Key, cloudApiSnapshot),
                string.Empty,
                string.Empty,
                string.Empty))
            .ToList();

        return new ModuleParamGroupSnapshot(
            CloudApiConfigParamSchema.ModuleId,
            CloudApiConfigParamSchema.GroupDisplayNameFallback,
            parameters,
            CloudApiConfigParamSchema.GroupDisplayNameResourceKey,
            CloudApiConfigParamSchema.GroupDisplayNameFallback);
    }

    private static List<ModuleParamGroupSnapshot> BuildModuleGroups(
        ModuleParamCategory category,
        IModuleParamRegistry moduleParamRegistry,
        IReadOnlyDictionary<string, string> moduleNames,
        IReadOnlyDictionary<string, string> moduleValues)
        => moduleParamRegistry.GetDescriptors(category)
            .Where(IsParamViewVisible)
            .GroupBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var moduleDisplayName = moduleNames.TryGetValue(group.Key, out var displayName)
                    ? displayName
                    : group.Key;
                var parameters = group
                    .OrderBy(x => x.SortOrder)
                    .Select(descriptor => new ModuleParamSnapshot(
                        descriptor.ModuleId,
                        descriptor.Category,
                        descriptor.StorageKey,
                        descriptor.Name,
                        descriptor.DisplayNameResourceKey ?? string.Empty,
                        descriptor.DisplayNameFallback ?? descriptor.Name,
                        descriptor.DescriptionResourceKey ?? string.Empty,
                        descriptor.DescriptionFallback ?? string.Empty,
                        descriptor.ValueKind,
                        moduleValues.TryGetValue(descriptor.StorageKey, out var configured)
                            ? configured
                            : descriptor.DefaultValue ?? string.Empty,
                        descriptor.DefaultValue ?? string.Empty,
                        descriptor.Unit ?? string.Empty,
                        descriptor.MinValue ?? string.Empty,
                        descriptor.MaxValue ?? string.Empty))
                    .ToList();

                return new ModuleParamGroupSnapshot(group.Key, moduleDisplayName, parameters);
            })
            .ToList();

    internal static bool IsParamViewVisible(ModuleParamDescriptor descriptor)
        => descriptor.Role != ModuleParamRole.MesSignToken;
}

public class SaveParamViewHandler(
    ISender sender,
    IClientPermissionService permissionService,
    IModuleParamRegistry moduleParamRegistry)
    : IRequestHandler<SaveParamViewCommand, CrudOperationResult>
{
    public async Task<CrudOperationResult> Handle(SaveParamViewCommand request, CancellationToken ct)
    {
        if (!permissionService.CanEditParams)
        {
            return CrudOperationResult.Failure("当前用户没有参数配置权限。");
        }

        var editableModuleKeys = new HashSet<string>(
            new[]
                {
                    ModuleParamCategory.Mes,
                    ModuleParamCategory.Cloud,
                    ModuleParamCategory.Business
                }
                .SelectMany(moduleParamRegistry.GetDescriptors)
                .Where(LoadParamViewHandler.IsParamViewVisible)
                .Select(static x => x.StorageKey),
            StringComparer.OrdinalIgnoreCase);

        var moduleParams = request.Params
            .Where(x => ModuleParamKeys.IsModuleStorageKey(x.Key) && editableModuleKeys.Contains(x.Key))
            .Select(static x => new ModuleParamDto(x.Key, x.Value, x.Description))
            .ToList();
        var cloudApiParams = request.Params
            .Where(static x => CloudApiConfigParamSchema.IsCloudApiConfigKey(x.Key)
                               && CloudApiConfigParamSchema.IsParamViewEditableKey(x.Key))
            .Select(static x => new CloudApiConfigParamDto(x.Key, x.Value, x.Description))
            .ToList();
        var invalidKeys = request.Params
            .Where(x => IsRejectedParamViewKey(x.Key, editableModuleKeys))
            .Select(static x => x.Key)
            .ToArray();
        if (invalidKeys.Length > 0)
        {
            return CrudOperationResult.Failure($"参数配置包含不允许保存的键：{string.Join(", ", invalidKeys)}。");
        }

        if (moduleParams.Count > 0)
        {
            var moduleResult = await sender.Send(new SaveModuleParamsCommand(moduleParams), ct);
            if (!moduleResult.IsSuccess)
            {
                return CrudOperationResult.Failure(moduleResult.ErrorMessage ?? "插件参数保存失败。");
            }
        }

        if (cloudApiParams.Count > 0)
        {
            var cloudApiResult = await sender.Send(new SaveCloudApiConfigParamsCommand(cloudApiParams), ct);
            if (!cloudApiResult.IsSuccess)
            {
                return CrudOperationResult.Failure(cloudApiResult.ErrorMessage ?? "云端接口配置保存失败。");
            }
        }

        return CrudOperationResult.Success("参数配置已保存。");
    }

    private static bool IsRejectedParamViewKey(string key, IReadOnlySet<string> editableModuleKeys)
    {
        if (ModuleParamKeys.IsModuleStorageKey(key))
        {
            return !editableModuleKeys.Contains(key);
        }

        if (CloudApiConfigParamSchema.IsCloudApiConfigKey(key))
        {
            return !CloudApiConfigParamSchema.IsParamViewEditableKey(key);
        }

        return true;
    }
}

public class ResetParamViewHandler(
    ISender sender,
    IClientPermissionService permissionService,
    IModuleParamRegistry moduleParamRegistry,
    ICloudApiConfigSnapshotProvider cloudApiConfigSnapshotProvider)
    : IRequestHandler<ResetParamViewCommand, CrudOperationResult>
{
    public async Task<CrudOperationResult> Handle(ResetParamViewCommand request, CancellationToken ct)
    {
        if (!permissionService.CanEditParams)
        {
            return CrudOperationResult.Failure("当前用户没有参数配置权限。");
        }

        var defaults = new[]
            {
                ModuleParamCategory.Mes,
                ModuleParamCategory.Cloud,
                ModuleParamCategory.Business
            }
            .SelectMany(category => moduleParamRegistry.GetDescriptors(category))
            .Where(LoadParamViewHandler.IsParamViewVisible)
            .OrderBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.Category)
            .ThenBy(static x => x.SortOrder)
            .Select(static x => new ModuleParamDto(
                x.StorageKey,
                x.DefaultValue ?? string.Empty,
                x.DescriptionFallback ?? x.DisplayNameFallback ?? x.Name))
            .ToList();

        if (defaults.Count > 0)
        {
            var moduleResult = await sender.Send(new SaveModuleParamsCommand(defaults), ct);
            if (!moduleResult.IsSuccess)
            {
                return CrudOperationResult.Failure(moduleResult.ErrorMessage ?? "插件参数重置失败。");
            }
        }

        var cloudApiSnapshot = cloudApiConfigSnapshotProvider.GetCurrent();
        var cloudApiDefaults = CloudApiConfigParamSchema.Descriptors
            .Where(static x => CloudApiConfigParamSchema.IsParamViewEditableKey(x.Key))
            .OrderBy(static x => x.SortOrder)
            .Select(x => new CloudApiConfigParamDto(
                x.Key,
                CloudApiConfigParamSchema.GetDefaultValue(x.Key, cloudApiSnapshot),
                x.DescriptionFallback))
            .ToList();
        var cloudApiResult = await sender.Send(new SaveCloudApiConfigParamsCommand(cloudApiDefaults), ct);
        if (!cloudApiResult.IsSuccess)
        {
            return CrudOperationResult.Failure(cloudApiResult.ErrorMessage ?? "云端接口配置重置失败。");
        }

        return CrudOperationResult.Success("参数配置已重置为默认值。");
    }
}
