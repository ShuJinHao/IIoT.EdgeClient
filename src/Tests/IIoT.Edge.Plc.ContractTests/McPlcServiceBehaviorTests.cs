using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Plc.ContractTests;

public sealed class McPlcServiceBehaviorTests
{
    [Theory]
    [InlineData("D700", "D", "700")]
    [InlineData("ZR400", "ZR", "400")]
    [InlineData("R300", "R", "300")]
    [InlineData("x1f", "X", "1F")]
    public void ParseAddress_ShouldSplitPrefixAndDeviceNumber(
        string address,
        string expectedPrefix,
        string expectedDeviceAddress)
    {
        var parsed = McPlcService.ParseAddress(address);

        Assert.Equal(expectedPrefix, parsed.Prefix.ToString());
        Assert.Equal(expectedDeviceAddress, parsed.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("700")]
    [InlineData("DABC")]
    [InlineData("UNKNOWN700")]
    public void ParseAddress_WhenAddressInvalid_ShouldReject(string address)
        => Assert.Throws<FormatException>(() => McPlcService.ParseAddress(address));

    [Fact]
    public async Task PlcServiceFactory_WhenMc_ShouldCreateMcpXBackedMcService()
    {
        var factory = new PlcServiceFactory(new FakeLogService(), new ModbusAddressParser());

        await using var service = factory.Create(PlcType.Mc, "PLC-MC");

        var target = typeof(PlcServiceProxy)
            .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service);
        Assert.IsType<McPlcService>(target);
    }

}
