using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Application.Abstractions.Plc.Factory;

/// <summary>
/// PLC 服务工厂契约。
/// 按 PLC 类型创建对应的通信服务实例。
/// </summary>
public interface IPlcServiceFactory
{
    IPlcService Create(PlcType plcType, string deviceName);
}
