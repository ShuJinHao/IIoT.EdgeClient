using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Application.Common.Crud;
using MediatR;

namespace IIoT.Edge.Application.Features.Config.ParamView;

/// <summary>
    /// 参数配置页增删改查服务契约，页面提交后由应用层按 key 分流到模块参数或宿主配置。
/// </summary>
public interface IParamViewCrudService
{
    Task<ParamViewInitResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<ParamViewValueDto> paramValues,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 参数配置页服务，负责将界面操作转发到模块参数查询与保存命令。
/// </summary>
public sealed class ParamViewCrudService(
    ISender sender,
    IClientPermissionService permissionService) : IParamViewCrudService
{
    public Task<ParamViewInitResult> LoadAsync(CancellationToken cancellationToken = default)
        => sender.Send(new LoadParamViewQuery(), cancellationToken);

    public Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<ParamViewValueDto> paramValues,
        CancellationToken cancellationToken = default)
    {
        if (!permissionService.CanEditParams)
        {
            return Task.FromResult(CrudOperationResult.Failure("当前用户没有参数配置权限。"));
        }

        return sender.Send(
            new SaveParamViewCommand(paramValues.ToList()),
            cancellationToken);
    }

    public Task<CrudOperationResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (!permissionService.CanEditParams)
        {
            return Task.FromResult(CrudOperationResult.Failure("当前用户没有参数配置权限。"));
        }

        return sender.Send(new ResetParamViewCommand(), cancellationToken);
    }
}
