namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// 编辑模型校验器契约。
/// </summary>
public interface IEditorValidator<in TEditModel>
{
    Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(TEditModel model, CancellationToken cancellationToken = default);
}
