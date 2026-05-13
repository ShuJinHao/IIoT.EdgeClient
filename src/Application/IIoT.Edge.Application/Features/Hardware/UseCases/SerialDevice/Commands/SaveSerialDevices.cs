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
                    var entity = SerialDeviceEntity.Create(
                        dto.DeviceName,
                        dto.DeviceType,
                        dto.PortName,
                        dto.BaudRate);
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

    private static void Apply(SerialDeviceEntity entity, SerialDeviceDto dto)
    {
        entity.Rename(dto.DeviceName);
        entity.ChangeDeviceType(dto.DeviceType);
        entity.UpdatePort(dto.PortName, dto.BaudRate, dto.DataBits, dto.StopBits, dto.Parity);
        entity.UpdateCommands(dto.SendCmd1, dto.SendCmd2);
        entity.SetEnabled(dto.IsEnabled);
        entity.UpdateRemark(dto.Remark);
    }

    private static string? Validate(SerialDeviceDto dto)
    {
        try
        {
            var entity = SerialDeviceEntity.Create(
                dto.DeviceName,
                dto.DeviceType,
                dto.PortName,
                dto.BaudRate);
            Apply(entity, dto);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
