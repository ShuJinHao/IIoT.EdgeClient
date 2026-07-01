using IIoT.Edge.Application.Common.Crud;
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
    string IpAddress,
    int Port1,
    int? Port2,
    string? SendCmd1,
    string? SendCmd2,
    int ConnectTimeout,
    bool IsEnabled,
    string? Remark,
    string? ProtocolFrame = null
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
        => await SubmittedEntityListSaveHelper.ReplaceSubmittedAsync(
            repo,
            request.Devices,
            ct => repo.GetListAsync(_ => true, ct),
            static dto => dto.Id,
            Validate,
            Create,
            Apply,
            cancellationToken).ConfigureAwait(false);

    private static NetworkDeviceEntity Create(NetworkDeviceDto dto)
        => NetworkDeviceEntity.Create(
            dto.DeviceName,
            dto.DeviceType,
            dto.IpAddress,
            dto.Port1);

    private static void Apply(NetworkDeviceEntity entity, NetworkDeviceDto dto)
    {
        entity.Rename(dto.DeviceName);
        entity.ChangeType(dto.DeviceType);
        entity.UpdateDeviceModel(dto.DeviceModel);
        entity.UpdateProtocolFrame(NormalizeProtocolFrame(dto));
        entity.UpdateEndpoint(dto.IpAddress, dto.Port1, dto.Port2, dto.ConnectTimeout);
        entity.UpdateCommands(dto.SendCmd1, dto.SendCmd2);
        entity.SetEnabled(dto.IsEnabled);
        entity.UpdateRemark(dto.Remark);
    }

    private static string? Validate(NetworkDeviceDto dto)
    {
        try
        {
            var entity = Create(dto);
            Apply(entity, dto);
            return ValidateProtocolFrame(dto);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static string? ValidateProtocolFrame(NetworkDeviceDto dto)
    {
        if (!IsMcPlc(dto) || string.IsNullOrWhiteSpace(dto.ProtocolFrame))
        {
            return null;
        }

        return IsSupportedMcFrame(dto.ProtocolFrame)
            ? null
            : "MC PLC 协议帧只支持 E3 或 E4。";
    }

    private static string? NormalizeProtocolFrame(NetworkDeviceDto dto)
        => IsMcPlc(dto) ? Normalize(dto.ProtocolFrame) : null;

    private static bool IsMcPlc(NetworkDeviceDto dto)
        => dto.DeviceType == DeviceType.PLC
           && string.Equals(dto.DeviceModel?.Trim(), PlcType.Mc.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedMcFrame(string value)
        => string.Equals(value.Trim(), nameof(IIoT.Edge.Application.Abstractions.Plc.McPlcFrameType.E3), StringComparison.OrdinalIgnoreCase)
           || string.Equals(value.Trim(), nameof(IIoT.Edge.Application.Abstractions.Plc.McPlcFrameType.E4), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
