using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

/// <summary>
/// 统一解析 PLC 通信端点。Modbus RTU 通过网络 PLC 的 Command1 绑定串口设备名称，串口参数仍来自串口设备配置。
/// </summary>
public sealed class PlcEndpointResolver(
    IReadRepository<SerialDeviceEntity> serialDevices) : IPlcEndpointResolver
{
    public async Task<PlcEndpoint> ResolveAsync(
        NetworkDeviceEntity device,
        PlcType plcType,
        CancellationToken cancellationToken = default)
    {
        if (plcType == PlcType.ModbusRtu)
        {
            return await ResolveModbusRtuEndpointAsync(device, cancellationToken).ConfigureAwait(false);
        }

        return new TcpPlcEndpoint(
            device.IpAddress,
            device.Port1,
            device.ConnectTimeout,
            ResolveMcFrameType(device, plcType));
    }

    private async Task<PlcEndpoint> ResolveModbusRtuEndpointAsync(
        NetworkDeviceEntity device,
        CancellationToken cancellationToken)
    {
        var serialDeviceName = device.SendCmd1?.Trim();
        if (string.IsNullOrWhiteSpace(serialDeviceName))
        {
            throw new InvalidOperationException(
                $"[PlcCode={device.PlcCode}] Modbus RTU PLC 必须在 Command1 中填写已配置的串口设备名称。");
        }

        var serialDevice = await serialDevices
            .GetAsync(
                x => x.DeviceName == serialDeviceName,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (serialDevice is null)
        {
            throw new InvalidOperationException(
                $"[PlcCode={device.PlcCode}] Modbus RTU 绑定的串口设备不存在：{serialDeviceName}。");
        }

        if (!serialDevice.IsEnabled)
        {
            throw new InvalidOperationException(
                $"[PlcCode={device.PlcCode}] Modbus RTU 绑定的串口设备已停用：{serialDeviceName}。");
        }

        var slaveId = NormalizeSlaveId(device.Port1, device.DeviceName);
        return new SerialPlcEndpoint(
            serialDevice.PortName,
            serialDevice.BaudRate,
            serialDevice.DataBits,
            serialDevice.StopBits,
            serialDevice.Parity,
            slaveId,
            device.ConnectTimeout);
    }

    private static byte NormalizeSlaveId(int value, string deviceName)
    {
        if (value is < 1 or > 247)
        {
            throw new InvalidOperationException(
                $"[{deviceName}] Modbus RTU 的 Port1 用作从站 ID，必须在 1 到 247 之间。");
        }

        return (byte)value;
    }

    private static McPlcFrameType ResolveMcFrameType(NetworkDeviceEntity device, PlcType plcType)
    {
        if (plcType != PlcType.Mc || string.IsNullOrWhiteSpace(device.ProtocolFrame))
        {
            return McPlcFrameType.E3;
        }

        if (Enum.TryParse<McPlcFrameType>(device.ProtocolFrame.Trim(), ignoreCase: true, out var frameType))
        {
            return frameType;
        }

        throw new InvalidOperationException(
            $"[PlcCode={device.PlcCode}] MC PLC 协议帧配置无效：{device.ProtocolFrame}，只支持 E3 或 E4。");
    }
}
