using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

/// <summary>从插件内存快照解析 TCP PLC 端点；v3 私库契约不暴露串口秘密或旧 Host 表。</summary>
public sealed class PlcEndpointResolver : IPlcEndpointResolver
{
    public Task<PlcEndpoint> ResolveAsync(
        DevicePluginPlcSnapshot device,
        PlcType plcType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plcType == PlcType.ModbusRtu)
        {
            throw new InvalidOperationException("PLUGIN_SERIAL_DEVICE_NOT_SUPPORTED");
        }

        PlcEndpoint endpoint = new TcpPlcEndpoint(
            device.IpAddress,
            device.Port1,
            device.ConnectTimeout,
            ResolveMcFrameType(device, plcType));
        return Task.FromResult(endpoint);
    }

    private static McPlcFrameType ResolveMcFrameType(
        DevicePluginPlcSnapshot device,
        PlcType plcType)
    {
        if (plcType != PlcType.Mc || string.IsNullOrWhiteSpace(device.ProtocolFrame))
        {
            return McPlcFrameType.E3;
        }

        if (Enum.TryParse<McPlcFrameType>(device.ProtocolFrame.Trim(), true, out var frameType))
        {
            return frameType;
        }

        throw new InvalidOperationException(
            $"[PlcCode={device.PlcCode}] MC PLC 协议帧配置无效：{device.ProtocolFrame}，只支持 E3 或 E4。");
    }
}
