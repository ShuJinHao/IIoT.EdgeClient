using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;
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
    List<ModuleParamSnapshot> Params);

public record ParamViewInitResult(
    List<ModuleParamGroupSnapshot> MesParamGroups,
    List<ModuleParamGroupSnapshot> CloudParamGroups,
    List<ModuleParamGroupSnapshot> BusinessParamGroups);

public record LoadParamViewQuery : IRequest<ParamViewInitResult>;

public record SaveParamViewCommand(List<ModuleParamDto> ModuleParams) : IRequest<CrudOperationResult>;

public class LoadParamViewHandler(
    ILocalParameterConfigService localParameterConfigService,
    IModuleParamRegistry moduleParamRegistry,
    IEnumerable<IEdgeProcessModule> modules)
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
            BuildModuleGroups(ModuleParamCategory.Cloud, moduleParamRegistry, moduleNames, moduleValues),
            BuildModuleGroups(ModuleParamCategory.Business, moduleParamRegistry, moduleNames, moduleValues));
    }

    private static List<ModuleParamGroupSnapshot> BuildModuleGroups(
        ModuleParamCategory category,
        IModuleParamRegistry moduleParamRegistry,
        IReadOnlyDictionary<string, string> moduleNames,
        IReadOnlyDictionary<string, string> moduleValues)
        => moduleParamRegistry.GetDescriptors(category)
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
}

public class SaveParamViewHandler(
    ISender sender,
    IClientPermissionService permissionService)
    : IRequestHandler<SaveParamViewCommand, CrudOperationResult>
{
    public async Task<CrudOperationResult> Handle(SaveParamViewCommand request, CancellationToken ct)
    {
        if (!permissionService.CanEditParams)
        {
            return CrudOperationResult.Failure("当前用户没有参数配置权限。");
        }

        var moduleResult = await sender.Send(new SaveModuleParamsCommand(request.ModuleParams), ct);
        if (!moduleResult.IsSuccess)
        {
            return CrudOperationResult.Failure(moduleResult.ErrorMessage ?? "插件参数保存失败。");
        }

        return CrudOperationResult.Success("参数配置已保存。");
    }
}
