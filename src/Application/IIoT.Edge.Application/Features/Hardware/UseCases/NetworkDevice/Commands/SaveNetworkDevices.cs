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

        foreach (var dto in request.Devices)
        {
            var validationError = Validate(dto);
            if (validationError is not null)
            {
                return Result.Failure(validationError);
            }
        }

        foreach (var entity in existingDevices.Where(x => !submittedIds.Contains(x.Id)))
        {
            repo.Delete(entity);
        }

        foreach (var dto in request.Devices)
        {
            try
            {
                if (dto.Id == 0)
                {
                    var entity = NetworkDeviceEntity.Create(
                        dto.DeviceName,
                        dto.DeviceType,
                        dto.IpAddress,
                        dto.Port1);
                    Apply(entity, dto);
                    repo.Add(entity);
                }
                else if (existingById.TryGetValue(dto.Id, out var entity))
                {
                    Apply(entity, dto);
                    repo.Update(entity);
                }
            }
            catch (ArgumentException ex)
            {
                return Result.Failure(ex.Message);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void Apply(NetworkDeviceEntity entity, NetworkDeviceDto dto)
    {
        entity.Rename(dto.DeviceName);
        entity.ChangeType(dto.DeviceType);
        entity.AssignModule(dto.ModuleId, dto.DeviceModel);
        entity.UpdateEndpoint(dto.IpAddress, dto.Port1, dto.Port2, dto.ConnectTimeout);
        entity.UpdateCommands(dto.SendCmd1, dto.SendCmd2);
        entity.SetEnabled(dto.IsEnabled);
        entity.UpdateRemark(dto.Remark);
    }

    private static string? Validate(NetworkDeviceDto dto)
    {
        try
        {
            var entity = NetworkDeviceEntity.Create(
                dto.DeviceName,
                dto.DeviceType,
                dto.IpAddress,
                dto.Port1);
            Apply(entity, dto);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
