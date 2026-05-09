using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;
using MediatR;

namespace IIoT.Edge.Application.Features.Config.ParamView;

public record ParamViewInitResult(
    List<ModuleParamGroupVm> MesParamGroups,
    List<ModuleParamGroupVm> CloudParamGroups,
    List<ModuleParamGroupVm> BusinessParamGroups);

public record LoadParamViewQuery : IRequest<ParamViewInitResult>;

public record SaveParamViewCommand(List<ModuleParamVm> ModuleParams) : IRequest<CrudOperationResult>;

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

    private static List<ModuleParamGroupVm> BuildModuleGroups(
        ModuleParamCategory category,
        IModuleParamRegistry moduleParamRegistry,
        IReadOnlyDictionary<string, string> moduleNames,
        IReadOnlyDictionary<string, string> moduleValues)
        => moduleParamRegistry.GetDescriptors(category)
            .GroupBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var vm = new ModuleParamGroupVm
                {
                    ModuleId = group.Key,
                    ModuleDisplayName = moduleNames.TryGetValue(group.Key, out var displayName)
                        ? displayName
                        : group.Key
                };

                foreach (var descriptor in group.OrderBy(x => x.SortOrder))
                {
                    vm.Params.Add(new ModuleParamVm
                    {
                        ModuleId = descriptor.ModuleId,
                        Category = descriptor.Category,
                        Key = descriptor.StorageKey,
                        Name = descriptor.Name,
                        DisplayNameResourceKey = descriptor.DisplayNameResourceKey ?? string.Empty,
                        DisplayNameFallback = descriptor.DisplayNameFallback ?? descriptor.Name,
                        DescriptionResourceKey = descriptor.DescriptionResourceKey ?? string.Empty,
                        DescriptionFallback = descriptor.DescriptionFallback ?? string.Empty,
                        DisplayName = descriptor.DisplayNameFallback ?? descriptor.Name,
                        Description = descriptor.DescriptionFallback ?? string.Empty,
                        ValueKind = descriptor.ValueKind,
                        Value = moduleValues.TryGetValue(descriptor.StorageKey, out var configured)
                            ? configured
                            : descriptor.DefaultValue ?? string.Empty,
                        DefaultValue = descriptor.DefaultValue ?? string.Empty,
                        Unit = descriptor.Unit ?? string.Empty,
                        Min = descriptor.MinValue ?? string.Empty,
                        Max = descriptor.MaxValue ?? string.Empty
                    });
                }

                return vm;
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

        var moduleParams = request.ModuleParams
            .Select(item => new ModuleParamDto(item.Key, item.Value))
            .ToList();
        var moduleResult = await sender.Send(new SaveModuleParamsCommand(moduleParams), ct);
        if (!moduleResult.IsSuccess)
        {
            return CrudOperationResult.Failure(moduleResult.ErrorMessage ?? "插件参数保存失败。");
        }

        return CrudOperationResult.Success("参数配置已保存。");
    }
}
