using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Plc.ContractTests;

public sealed class ModbusPlcServiceBehaviorTests
{
    private readonly ModbusAddressParser _addressParser = new();

    [Theory]
    [InlineData("HR0", 1, ModbusAddressKind.HoldingRegister, 0)]
    [InlineData("2:HR10", 2, ModbusAddressKind.HoldingRegister, 10)]
    [InlineData("IR3", 1, ModbusAddressKind.InputRegister, 3)]
    [InlineData("C5", 1, ModbusAddressKind.Coil, 5)]
    [InlineData("DI7", 1, ModbusAddressKind.DiscreteInput, 7)]
    [InlineData("40001", 1, ModbusAddressKind.HoldingRegister, 0)]
    [InlineData("30002", 1, ModbusAddressKind.InputRegister, 1)]
    [InlineData("00003", 1, ModbusAddressKind.Coil, 2)]
    [InlineData("10004", 1, ModbusAddressKind.DiscreteInput, 3)]
    [InlineData("4x1", 1, ModbusAddressKind.HoldingRegister, 0)]
    public void ModbusAddressParser_ShouldParseSupportedAddressForms(
        string address,
        byte expectedSlaveId,
        ModbusAddressKind expectedKind,
        ushort expectedOffset)
    {
        var parsed = _addressParser.Parse(address);

        Assert.Equal(expectedSlaveId, parsed.SlaveId);
        Assert.Equal(expectedKind, parsed.Kind);
        Assert.Equal(expectedOffset, parsed.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0:HR1")]
    [InlineData("HR")]
    [InlineData("50001")]
    public void ModbusAddressParser_WhenAddressInvalid_ShouldReject(string address)
    {
        Assert.Throws<FormatException>(() => _addressParser.Parse(address));
    }

    [Theory]
    [InlineData(PlcType.ModbusTcp, typeof(ModbusPlcService))]
    [InlineData(PlcType.ModbusRtu, typeof(ModbusPlcService))]
    public async Task PlcServiceFactory_ShouldCreateModbusServices(PlcType plcType, Type expectedType)
    {
        var factory = new PlcServiceFactory(new FakeLogService(), _addressParser);

        await using var service = factory.Create(plcType, "PLC-MODBUS");

        var proxyTarget = typeof(IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.PlcServiceProxy)
            .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service);
        Assert.IsType(expectedType, proxyTarget);
    }

    [Fact]
    public async Task ModbusTcpService_WhenInitializedWithSerialEndpoint_ShouldReject()
    {
        await using var service = new ModbusPlcService(ModbusTransportKind.Tcp, _addressParser);

        Assert.Throws<ArgumentException>(() => service.Init(
            new SerialPlcEndpoint("COM1", 9600, 8, "One", "None")));
    }

    [Fact]
    public async Task ModbusRtuService_WhenInitializedWithTcpEndpoint_ShouldReject()
    {
        await using var service = new ModbusPlcService(ModbusTransportKind.Rtu, _addressParser);

        Assert.Throws<ArgumentException>(() => service.Init(
            new TcpPlcEndpoint("127.0.0.1", 502)));
    }

    [Fact]
    public async Task PlcEndpointResolver_WhenModbusRtu_ShouldFailClosedWithoutLegacyHostSerialTables()
    {
        var resolver = new PlcEndpointResolver();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(
                CreatePluginPlc("PLC-RTU", PlcType.ModbusRtu, 7, 5000),
                PlcType.ModbusRtu,
                TestContext.Current.CancellationToken));

        Assert.Equal("PLUGIN_SERIAL_DEVICE_NOT_SUPPORTED", exception.Message);
    }

    [Fact]
    public async Task PlcEndpointResolver_WhenModbusTcp_ShouldUsePluginSnapshotEndpoint()
    {
        var resolver = new PlcEndpointResolver();

        var endpoint = await resolver.ResolveAsync(
            CreatePluginPlc("PLC-TCP", PlcType.ModbusTcp, 502, 4500),
            PlcType.ModbusTcp,
            TestContext.Current.CancellationToken);

        var tcp = Assert.IsType<TcpPlcEndpoint>(endpoint);
        Assert.Equal("127.0.0.1", tcp.Host);
        Assert.Equal(502, tcp.Port);
        Assert.Equal(4500, tcp.ConnectTimeoutMs);
    }

    private static DevicePluginPlcSnapshot CreatePluginPlc(
        string plcCode,
        PlcType plcType,
        int port,
        int connectTimeout)
        => new(
            1,
            new DevicePluginPlcConfiguration(
                plcCode,
                plcCode,
                "PLC",
                plcType.ToString(),
                null,
                "127.0.0.1",
                port,
                null,
                connectTimeout,
                true,
                null));
}
