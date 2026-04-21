using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using IIoT.Edge.Application.Features.Hardware.Queries;
using MediatR;

namespace IIoT.Edge.Application.Features.Config.ParamView;

public record DeviceGroupHeader(int DeviceId, string DeviceName);

public record ParamViewInitResult(
    List<GeneralParamVm> GeneralParams,
    List<DeviceGroupHeader> DeviceGroups);

public record LoadParamViewQuery : IRequest<ParamViewInitResult>;

public record LoadDeviceParamsQuery(int DeviceId) : IRequest<List<DeviceParamVm>>;

public record SaveParamViewCommand(
    List<GeneralParamVm> GeneralParams,
    int DeviceId,
    List<DeviceParamVm> DeviceParams) : IRequest<CrudOperationResult>;

public class LoadParamViewHandler(
    ISender sender,
    ILocalParameterConfigService localParameterConfigService)
    : IRequestHandler<LoadParamViewQuery, ParamViewInitResult>
{
    public async Task<ParamViewInitResult> Handle(LoadParamViewQuery request, CancellationToken ct)
    {
        var general = (await localParameterConfigService.GetSystemConfigsAsync(ct))
            .Select(snapshot => new GeneralParamVm
            {
                Id = snapshot.Id,
                Key = snapshot.Key,
                Name = snapshot.Key,
                Value = snapshot.Value,
                Description = snapshot.Description ?? string.Empty
            })
            .ToList();

        var devResult = await sender.Send(new GetAllNetworkDevicesQuery(), ct);
        var groups = new List<DeviceGroupHeader>();
        if (devResult.IsSuccess && devResult.Value != null)
        {
            foreach (var device in devResult.Value.Where(x => x.IsEnabled))
            {
                groups.Add(new DeviceGroupHeader(device.Id, $"{device.DeviceName} ({device.IpAddress})"));
            }
        }

        return new ParamViewInitResult(general, groups);
    }
}

public class LoadDeviceParamsHandler(
    ILocalParameterConfigService localParameterConfigService)
    : IRequestHandler<LoadDeviceParamsQuery, List<DeviceParamVm>>
{
    public async Task<List<DeviceParamVm>> Handle(LoadDeviceParamsQuery request, CancellationToken ct)
        => (await localParameterConfigService.GetDeviceParamsAsync(request.DeviceId, ct))
            .Select(snapshot => new DeviceParamVm
            {
                Id = snapshot.Id,
                Name = snapshot.Name,
                Value = snapshot.Value,
                Unit = snapshot.Unit ?? string.Empty,
                Min = snapshot.MinValue ?? string.Empty,
                Max = snapshot.MaxValue ?? string.Empty
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
            return CrudOperationResult.Failure("当前用户无参数配置权限。");
        }

        var systemConfigs = request.GeneralParams
            .Select(item => new SystemConfigDto(
                item.Name,
                item.Value,
                string.IsNullOrWhiteSpace(item.Description) ? null : item.Description))
            .ToList();
        var systemResult = await sender.Send(new SaveSystemConfigsCommand(systemConfigs), ct);
        if (!systemResult.IsSuccess)
        {
            return CrudOperationResult.Failure(systemResult.ErrorMessage ?? "系统参数保存失败。");
        }

        var deviceParams = request.DeviceParams
            .Select(item => new DeviceParamDto(
                item.Name,
                item.Value,
                string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit,
                string.IsNullOrWhiteSpace(item.Min) ? null : item.Min,
                string.IsNullOrWhiteSpace(item.Max) ? null : item.Max))
            .ToList();
        var deviceResult = await sender.Send(new SaveDeviceParamsCommand(request.DeviceId, deviceParams), ct);
        if (!deviceResult.IsSuccess)
        {
            return CrudOperationResult.Failure(deviceResult.ErrorMessage ?? "设备参数保存失败。");
        }

        return CrudOperationResult.Success("已保存到本地参数配置。");
    }
}
