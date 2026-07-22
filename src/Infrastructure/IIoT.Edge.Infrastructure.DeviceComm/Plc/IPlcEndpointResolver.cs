using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Hardware;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

/// <summary>
/// 根据硬件配置解析 PLC 运行时通信端点。
/// </summary>
public interface IPlcEndpointResolver
{
    Task<PlcEndpoint> ResolveAsync(
        NetworkDeviceEntity device,
        PlcType plcType,
        CancellationToken cancellationToken = default);
}
