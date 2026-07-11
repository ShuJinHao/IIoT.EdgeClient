using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Specification;

namespace IIoT.Edge.NonUiRegressionTests;

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
    public void PlcServiceFactory_ShouldCreateModbusServices(PlcType plcType, Type expectedType)
    {
        var factory = new PlcServiceFactory(new FakeLogService(), _addressParser);

        using var service = factory.Create(plcType, "PLC-MODBUS");

        var proxyTarget = typeof(IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.PlcServiceProxy)
            .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service);
        Assert.IsType(expectedType, proxyTarget);
    }

    [Fact]
    public void ModbusTcpService_WhenInitializedWithSerialEndpoint_ShouldReject()
    {
        using var service = new ModbusPlcService(ModbusTransportKind.Tcp, _addressParser);

        Assert.Throws<ArgumentException>(() => service.Init(
            new SerialPlcEndpoint("COM1", 9600, 8, "One", "None")));
    }

    [Fact]
    public void ModbusRtuService_WhenInitializedWithTcpEndpoint_ShouldReject()
    {
        using var service = new ModbusPlcService(ModbusTransportKind.Rtu, _addressParser);

        Assert.Throws<ArgumentException>(() => service.Init(
            new TcpPlcEndpoint("127.0.0.1", 502)));
    }

    [Fact]
    public async Task PlcEndpointResolver_WhenModbusRtu_ShouldUseBoundSerialDevice()
    {
        var serialDevice = SerialDeviceEntity.Create("RTU-COM3", "Modbus RTU", "COM3", 19200);
        serialDevice.UpdatePort("COM3", 19200, 8, "One", "None");
        var resolver = new PlcEndpointResolver(new SerialDeviceReadRepository(serialDevice));
        var plc = NetworkDeviceEntity.Create("PLC-RTU", DeviceType.PLC, "127.0.0.1", 7);
        plc.UpdateDeviceModel(PlcType.ModbusRtu.ToString());
        plc.UpdateCommands("RTU-COM3", null);
        plc.UpdateEndpoint("127.0.0.1", 7, null, 5000);

        var endpoint = await resolver.ResolveAsync(
            plc,
            PlcType.ModbusRtu,
            TestContext.Current.CancellationToken);

        var serialEndpoint = Assert.IsType<SerialPlcEndpoint>(endpoint);
        Assert.Equal("COM3", serialEndpoint.PortName);
        Assert.Equal(19200, serialEndpoint.BaudRate);
        Assert.Equal((byte)7, serialEndpoint.SlaveId);
        Assert.Equal(5000, serialEndpoint.ConnectTimeoutMs);
    }

    [Fact]
    public async Task PlcEndpointResolver_WhenModbusRtuHasNoSerialBinding_ShouldReject()
    {
        var resolver = new PlcEndpointResolver(new SerialDeviceReadRepository());
        var plc = NetworkDeviceEntity.Create("PLC-RTU", DeviceType.PLC, "127.0.0.1", 1);
        plc.UpdateDeviceModel(PlcType.ModbusRtu.ToString());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(
                plc,
                PlcType.ModbusRtu,
                TestContext.Current.CancellationToken));

        Assert.Contains("Command1", exception.Message);
    }

    private sealed class SerialDeviceReadRepository(params SerialDeviceEntity[] devices) : IReadRepository<SerialDeviceEntity>
    {
        private readonly List<SerialDeviceEntity> _devices = [.. devices];

        public IQueryable<SerialDeviceEntity> GetQueryable()
            => _devices.AsQueryable();

        public Task<SerialDeviceEntity?> GetByIdAsync<TKey>(
            TKey id,
            CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult<SerialDeviceEntity?>(null);

        public Task<SerialDeviceEntity?> GetAsync(
            Expression<Func<SerialDeviceEntity, bool>> expression,
            Expression<Func<SerialDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.AsQueryable().FirstOrDefault(expression));

        public Task<List<SerialDeviceEntity>> GetListAsync(
            Expression<Func<SerialDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.AsQueryable().Where(expression).ToList());

        public Task<List<SerialDeviceEntity>> GetListAsync(
            Expression<Func<SerialDeviceEntity, bool>> expression,
            Expression<Func<SerialDeviceEntity, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => GetListAsync(expression, cancellationToken);

        public Task<List<SerialDeviceEntity>> GetListAsync(
            ISpecification<SerialDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.ToList());

        public Task<SerialDeviceEntity?> GetSingleOrDefaultAsync(
            ISpecification<SerialDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.SingleOrDefault());

        public Task<int> GetCountAsync(
            Expression<Func<SerialDeviceEntity, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.AsQueryable().Count(expression));

        public Task<int> CountAsync(
            ISpecification<SerialDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.Count);

        public Task<bool> AnyAsync(
            ISpecification<SerialDeviceEntity>? specification = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_devices.Count > 0);
    }
}
