using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;

/// <summary>
/// 单条网络设备的数据传输对象。
/// </summary>
public record NetworkDeviceDto(
    int Id,
    string DeviceName,
    DeviceType DeviceType,
    string? DeviceModel,
    string ModuleId,
    string IpAddress,
    int Port1,
    int? Port2,
    string? SendCmd1,
    string? SendCmd2,
    int ConnectTimeout,
    bool IsEnabled,
    string? Remark
);

/// <summary>
/// 命令：保存网络设备列表，按提交结果进行新增或更新。
/// </summary>
public record SaveNetworkDevicesCommand(
    List<NetworkDeviceDto> Devices
) : ICommand<Result>;

/// <summary>
/// 处理器：保存网络设备配置。
/// </summary>
public class SaveNetworkDevicesHandler(
    IRepository<NetworkDeviceEntity> repo
) : ICommandHandler<SaveNetworkDevicesCommand, Result>
{
    public async Task<Result> Handle(
        SaveNetworkDevicesCommand request,
        CancellationToken cancellationToken)
    {
        var existingDevices = await repo.GetListAsync(_ => true, cancellationToken);
        var existingById = existingDevices.ToDictionary(x => x.Id);
        var submittedIds = request.Devices
            .Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        foreach (var entity in existingDevices.Where(x => !submittedIds.Contains(x.Id)))
        {
            repo.Delete(entity);
        }

        foreach (var dto in request.Devices)
        {
            if (dto.Id == 0)
            {
                var entity = new NetworkDeviceEntity(
                    dto.DeviceName, dto.DeviceType, dto.IpAddress, dto.Port1)
                {
                    DeviceModel = dto.DeviceModel,
                    ModuleId = dto.ModuleId,
                    Port2 = dto.Port2,
                    SendCmd1 = dto.SendCmd1,
                    SendCmd2 = dto.SendCmd2,
                    ConnectTimeout = dto.ConnectTimeout,
                    IsEnabled = dto.IsEnabled,
                    Remark = dto.Remark
                };
                repo.Add(entity);
            }
            else if (existingById.TryGetValue(dto.Id, out var entity))
            {
                entity.DeviceName = dto.DeviceName;
                entity.DeviceType = dto.DeviceType;
                entity.DeviceModel = dto.DeviceModel;
                entity.ModuleId = dto.ModuleId;
                entity.IpAddress = dto.IpAddress;
                entity.Port1 = dto.Port1;
                entity.Port2 = dto.Port2;
                entity.SendCmd1 = dto.SendCmd1;
                entity.SendCmd2 = dto.SendCmd2;
                entity.ConnectTimeout = dto.ConnectTimeout;
                entity.IsEnabled = dto.IsEnabled;
                entity.Remark = dto.Remark;
                repo.Update(entity);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
