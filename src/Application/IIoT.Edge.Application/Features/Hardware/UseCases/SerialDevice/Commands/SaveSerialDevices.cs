using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;

/// <summary>
/// 单条串口设备的数据传输对象。
/// </summary>
public record SerialDeviceDto(
    int Id,
    string DeviceName,
    string DeviceType,
    string PortName,
    int BaudRate,
    int DataBits,
    string StopBits,
    string Parity,
    string? SendCmd1,
    string? SendCmd2,
    bool IsEnabled,
    string? Remark
);

/// <summary>
/// 命令：保存串口设备列表，按提交结果进行新增或更新。
/// </summary>
public record SaveSerialDevicesCommand(
    List<SerialDeviceDto> Devices
) : ICommand<Result>;

/// <summary>
/// 处理器：保存串口设备配置。
/// </summary>
public class SaveSerialDevicesHandler(
    IRepository<SerialDeviceEntity> repo
) : ICommandHandler<SaveSerialDevicesCommand, Result>
{
    public async Task<Result> Handle(
        SaveSerialDevicesCommand request,
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
                var entity = new SerialDeviceEntity(
                    dto.DeviceName, dto.DeviceType, dto.PortName, dto.BaudRate)
                {
                    DataBits = dto.DataBits,
                    StopBits = dto.StopBits,
                    Parity = dto.Parity,
                    SendCmd1 = dto.SendCmd1,
                    SendCmd2 = dto.SendCmd2,
                    IsEnabled = dto.IsEnabled,
                    Remark = dto.Remark
                };
                repo.Add(entity);
            }
            else if (existingById.TryGetValue(dto.Id, out var entity))
            {
                entity.DeviceName = dto.DeviceName;
                entity.DeviceType = dto.DeviceType;
                entity.PortName = dto.PortName;
                entity.BaudRate = dto.BaudRate;
                entity.DataBits = dto.DataBits;
                entity.StopBits = dto.StopBits;
                entity.Parity = dto.Parity;
                entity.SendCmd1 = dto.SendCmd1;
                entity.SendCmd2 = dto.SendCmd2;
                entity.IsEnabled = dto.IsEnabled;
                entity.Remark = dto.Remark;
                repo.Update(entity);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
