using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;

internal sealed class GeneralParamValidator : IEditorValidator<GeneralParamVm>
{
    private readonly Func<string, string, string> _getText;

    public GeneralParamValidator(Func<string, string, string> getText)
    {
        _getText = getText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        GeneralParamVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            issues.Add(new ValidationIssue(
                _getText("Navigation_Param_Validation_GeneralNameRequired", "通用参数名称不能为空。"),
                nameof(model.Name)));
        }

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

internal sealed class DeviceParamValidator : IEditorValidator<DeviceParamVm>
{
    private readonly Func<string, string, string> _getText;

    public DeviceParamValidator(Func<string, string, string> getText)
    {
        _getText = getText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        DeviceParamVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            issues.Add(new ValidationIssue(
                _getText("Navigation_Param_Validation_DeviceNameRequired", "设备参数名称不能为空。"),
                nameof(model.Name)));
        }

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}
