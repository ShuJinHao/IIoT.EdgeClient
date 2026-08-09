namespace IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;

/// <summary>
/// 串口设备编辑 DTO 仅保留给 UI 显示兼容；正式 v3 插件数据库契约不支持 Host 持久化串口设备。
/// </summary>
public sealed record SerialDeviceDto(
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
    string? Remark);
