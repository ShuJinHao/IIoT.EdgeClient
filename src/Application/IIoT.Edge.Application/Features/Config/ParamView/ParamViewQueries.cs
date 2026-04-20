using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Common.Crud;
using AutoMapper;
using IIoT.Edge.Application.Features.Config.ParamView.Mappings;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Queries;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
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

public class LoadParamViewHandler(ISender sender, IMapper mapper)
    : IRequestHandler<LoadParamViewQuery, ParamViewInitResult>
{
    public async Task<ParamViewInitResult> Handle(LoadParamViewQuery request, CancellationToken ct)
    {
        var sysResult = await sender.Send(new GetAllSystemConfigsQuery(), ct);
        var general = new List<GeneralParamVm>();
        if (sysResult.IsSuccess && sysResult.Value != null)
        {
            foreach (var entity in sysResult.Value.OrderBy(x => x.SortOrder))
            {
                general.Add(mapper.Map<GeneralParamVm>(entity));
            }
        }

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

public class LoadDeviceParamsHandler(ISender sender, IMapper mapper)
    : IRequestHandler<LoadDeviceParamsQuery, List<DeviceParamVm>>
{
    public async Task<List<DeviceParamVm>> Handle(LoadDeviceParamsQuery request, CancellationToken ct)
    {
        var result = await sender.Send(new GetDeviceParamsQuery(request.DeviceId), ct);
        if (!result.IsSuccess || result.Value is null)
        {
            return new();
        }

        return result.Value
            .OrderBy(x => x.SortOrder)
            .Select(entity => mapper.Map<DeviceParamVm>(entity))
            .ToList();
    }
}

public class SaveParamViewHandler(
    ISender sender,
    IMapper mapper,
    IClientPermissionService permissionService)
    : IRequestHandler<SaveParamViewCommand, CrudOperationResult>
{
    public async Task<CrudOperationResult> Handle(SaveParamViewCommand request, CancellationToken ct)
    {
        if (!permissionService.CanEditParams)
        {
            return CrudOperationResult.Failure("当前用户无参数配置权限。");
        }

        var systemConfigs = mapper.Map<List<SystemConfigDto>>(request.GeneralParams);
        var systemResult = await sender.Send(new SaveSystemConfigsCommand(systemConfigs), ct);
        if (!systemResult.IsSuccess)
        {
            return CrudOperationResult.Failure(systemResult.ErrorMessage ?? "系统参数保存失败。");
        }

        var deviceParams = mapper.Map<List<DeviceParamDto>>(request.DeviceParams);
        var deviceResult = await sender.Send(new SaveDeviceParamsCommand(request.DeviceId, deviceParams), ct);
        if (!deviceResult.IsSuccess)
        {
            return CrudOperationResult.Failure(deviceResult.ErrorMessage ?? "设备参数保存失败。");
        }

        return CrudOperationResult.Success("参数配置已保存。");
    }
}
