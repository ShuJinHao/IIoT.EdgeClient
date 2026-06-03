using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;

public interface IParamViewModelMapper
{
    ModuleParamGroupVm ToGroup(ModuleParamGroupSnapshot snapshot);

    ParamViewValueDto ToDto(ModuleParamVm model);
}

public sealed class ParamViewModelMapper : IParamViewModelMapper
{
    public ModuleParamGroupVm ToGroup(ModuleParamGroupSnapshot snapshot)
    {
        var group = new ModuleParamGroupVm
        {
            ModuleId = snapshot.ModuleId,
            ModuleDisplayName = snapshot.ModuleDisplayNameFallback.Length > 0
                ? snapshot.ModuleDisplayNameFallback
                : snapshot.ModuleDisplayName,
            ModuleDisplayNameResourceKey = snapshot.ModuleDisplayNameResourceKey,
            ModuleDisplayNameFallback = snapshot.ModuleDisplayNameFallback.Length > 0
                ? snapshot.ModuleDisplayNameFallback
                : snapshot.ModuleDisplayName
        };

        foreach (var parameter in snapshot.Params.Select(ToParam))
        {
            group.Params.Add(parameter);
        }

        return group;
    }

    public ParamViewValueDto ToDto(ModuleParamVm model)
        => new(model.Key, model.Value);

    private static ModuleParamVm ToParam(ModuleParamSnapshot snapshot)
        => new()
        {
            ModuleId = snapshot.ModuleId,
            Category = snapshot.Category,
            Key = snapshot.Key,
            Name = snapshot.Name,
            DisplayNameResourceKey = snapshot.DisplayNameResourceKey,
            DisplayNameFallback = snapshot.DisplayNameFallback,
            DescriptionResourceKey = snapshot.DescriptionResourceKey,
            DescriptionFallback = snapshot.DescriptionFallback,
            DisplayName = snapshot.DisplayNameFallback,
            Description = snapshot.DescriptionFallback,
            ValueKind = snapshot.ValueKind,
            Value = snapshot.Value,
            DefaultValue = snapshot.DefaultValue,
            Unit = snapshot.Unit,
            Min = snapshot.Min,
            Max = snapshot.Max
        };
}
