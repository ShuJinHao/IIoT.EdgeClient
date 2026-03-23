using IIoT.Edge.Common.Enums;
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;
using IIoT.Edge.Contracts.Plc.Factory;
using IIoT.Edge.PlcDevice.Services;

namespace IIoT.Edge.PlcDevice.Proxy;

public class PlcServiceFactory : IPlcServiceFactory
{
    private readonly ILogService _logger;

    public PlcServiceFactory(ILogService logger)
    {
        _logger = logger;
    }

    public IPlcService Create(PlcType plcType, string deviceName)
    {
        IPlcService service = plcType switch
        {
            PlcType.Mc => new McPlcService(),
            PlcType.S7 => new S7PlcService(),
            _ => throw new NotSupportedException($"不支持的PLC类型: {plcType}")
        };

        return new PlcServiceProxy(service, _logger, deviceName);
    }
}