using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Domain.Tests;

public sealed class HardwareEntityValidationTests
{
    [Theory]
    [InlineData("", "192.168.0.10", 102)]
    [InlineData("PLC-A", "", 102)]
    [InlineData("PLC-A", "192.168.0.10", 0)]
    public void NetworkDeviceEntity_WhenRequiredFieldsInvalid_ShouldReject(
        string deviceName,
        string ipAddress,
        int port)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, ipAddress, port));
    }

    [Theory]
    [InlineData("", 9600, 8, "One", "None")]
    [InlineData("COM1", 0, 8, "One", "None")]
    [InlineData("COM1", 9600, 0, "One", "None")]
    [InlineData("COM1", 9600, 8, "", "None")]
    [InlineData("COM1", 9600, 8, "One", "")]
    public void SerialDeviceEntity_WhenPortFieldsInvalid_ShouldReject(
        string portName,
        int baudRate,
        int dataBits,
        string stopBits,
        string parity)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SerialDeviceEntity.Create("Scanner-A", "Scanner", portName, baudRate)
                .UpdatePort(portName, baudRate, dataBits, stopBits, parity));
    }

    [Theory]
    [InlineData(0, "Signal.A", "D0", 1, "Int16", "Read")]
    [InlineData(1, "", "D0", 1, "Int16", "Read")]
    [InlineData(1, "Signal.A", "D0", 0, "Int16", "Read")]
    [InlineData(1, "Signal.A", "D0", 1, "", "Read")]
    [InlineData(1, "Signal.A", "D0", 1, "Int16", "")]
    public void IoMappingEntity_WhenRequiredFieldsInvalid_ShouldReject(
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            IoMappingEntity.Create(networkDeviceId, signalKey, plcAddress, addressCount, dataType, direction));
    }

    [Fact]
    public void IoMappingEntity_WhenPlcAddressEmpty_ShouldKeepUnconfiguredState()
    {
        var entity = IoMappingEntity.Create(1, "Signal.A", "", 1, "Int16", "Read");

        Assert.Equal(string.Empty, entity.PlcAddress);
    }
}
