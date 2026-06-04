using IIoT.Edge.Application.Common.Crud;
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
        => await SubmittedEntityListSaveHelper.ReplaceSubmittedAsync(
            repo,
            request.Devices,
            ct => repo.GetListAsync(_ => true, ct),
            static dto => dto.Id,
            Validate,
            Create,
            Apply,
            cancellationToken).ConfigureAwait(false);

    private static SerialDeviceEntity Create(SerialDeviceDto dto)
        => SerialDeviceEntity.Create(
            dto.DeviceName,
            dto.DeviceType,
            dto.PortName,
            dto.BaudRate);

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
            var entity = Create(dto);
            Apply(entity, dto);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
