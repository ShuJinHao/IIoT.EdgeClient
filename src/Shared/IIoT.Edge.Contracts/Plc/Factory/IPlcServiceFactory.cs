using IIoT.Edge.Common.Enums;

namespace IIoT.Edge.Contracts.Plc.Factory;

public interface IPlcServiceFactory
{
    IPlcService Create(PlcType plcType, string deviceName);
}