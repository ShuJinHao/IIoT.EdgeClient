using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using MediatR;

namespace IIoT.Edge.Application.Features.Config.ParamView;

/// <summary>
/// 参数页面增删改查服务契约。
/// </summary>
public interface IParamViewCrudService
{
    Task<ParamViewInitResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<List<DeviceParamVm>> LoadDeviceParamsAsync(int deviceId, CancellationToken cancellationToken = default);

    Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<GeneralParamVm> generalParams,
        int deviceId,
        IReadOnlyCollection<DeviceParamVm> deviceParams,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 参数页面增删改查服务。
/// 负责将界面操作转发到参数查询与保存命令。
/// </summary>
public sealed class ParamViewCrudService(
    ISender sender,
    IClientPermissionService permissionService) : IParamViewCrudService
{
    public Task<ParamViewInitResult> LoadAsync(CancellationToken cancellationToken = default)
        => sender.Send(new LoadParamViewQuery(), cancellationToken);

    public Task<List<DeviceParamVm>> LoadDeviceParamsAsync(int deviceId, CancellationToken cancellationToken = default)
        => sender.Send(new LoadDeviceParamsQuery(deviceId), cancellationToken);

    public Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<GeneralParamVm> generalParams,
        int deviceId,
        IReadOnlyCollection<DeviceParamVm> deviceParams,
        CancellationToken cancellationToken = default)
    {
        if (!permissionService.CanEditParams)
        {
            return Task.FromResult(CrudOperationResult.Failure("当前用户无参数配置权限。"));
        }

        return sender.Send(
            new SaveParamViewCommand(
                generalParams.ToList(),
                deviceId,
                deviceParams.ToList()),
            cancellationToken);
    }
}
