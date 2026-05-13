using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;

public class PlcServiceFactory : IPlcServiceFactory
{
    private readonly ILogService _logger;
    private readonly IModbusAddressParser _modbusAddressParser;

    public PlcServiceFactory(
        ILogService logger,
        IModbusAddressParser modbusAddressParser)
    {
        _logger = logger;
        _modbusAddressParser = modbusAddressParser;
    }

    public IPlcService Create(PlcType plcType, string deviceName)
    {
        IPlcService service = plcType switch
        {
            PlcType.Mc => new McPlcService(),
            PlcType.S7 => new S7PlcService(),
            PlcType.ModbusTcp => new ModbusPlcService(ModbusTransportKind.Tcp, _modbusAddressParser),
            PlcType.ModbusRtu => new ModbusPlcService(ModbusTransportKind.Rtu, _modbusAddressParser),
            _ => throw new NotSupportedException($"不支持的 PLC 类型: {plcType}")
        };

        return new PlcServiceProxy(service, _logger, deviceName);
    }
}
