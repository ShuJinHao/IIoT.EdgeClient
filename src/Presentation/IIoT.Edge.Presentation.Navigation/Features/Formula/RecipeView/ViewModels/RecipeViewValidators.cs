using IIoT.Edge.Application.Common.Crud;

namespace IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;

/// <summary>
/// 本地配方参数编辑模型。
/// </summary>
internal sealed record LocalRecipeParamEditModel(
    string Key,
    string Min,
    string Max,
    string Unit);

/// <summary>
/// 本地配方参数校验器。
/// </summary>
internal sealed class LocalRecipeParamValidator : IEditorValidator<LocalRecipeParamEditModel>
{
    private readonly Func<string, string, string> _getText;

    public LocalRecipeParamValidator(Func<string, string, string> getText)
    {
        _getText = getText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        LocalRecipeParamEditModel model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.Key))
            issues.Add(new ValidationIssue(
                _getText("Navigation_Recipe_Validation_ParamNameRequired", "本地配方参数名称不能为空。"),
                nameof(model.Key)));

        var hasMin = !string.IsNullOrWhiteSpace(model.Min);
        var hasMax = !string.IsNullOrWhiteSpace(model.Max);

        double minValue = 0;
        double maxValue = 0;
        var minValid = !hasMin || double.TryParse(model.Min, out minValue);
        var maxValid = !hasMax || double.TryParse(model.Max, out maxValue);

        if (!minValid)
            issues.Add(new ValidationIssue(
                _getText("Navigation_Recipe_Validation_MinNumber", "最小值必须是有效数字。"),
                nameof(model.Min)));

        if (!maxValid)
            issues.Add(new ValidationIssue(
                _getText("Navigation_Recipe_Validation_MaxNumber", "最大值必须是有效数字。"),
                nameof(model.Max)));

        if (minValid && maxValid && hasMin && hasMax && minValue > maxValue)
            issues.Add(new ValidationIssue(
                _getText("Navigation_Recipe_Validation_MinLessEqualMax", "最小值不能大于最大值。")));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}
