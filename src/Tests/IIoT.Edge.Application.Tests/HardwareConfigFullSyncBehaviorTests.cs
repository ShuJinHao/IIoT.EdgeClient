using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Application.Tests;

public sealed class HardwareConfigFullSyncBehaviorTests
{
    [Fact]
    public async Task LoadHardwareConfig_UsesPluginSnapshotAndDoesNotExposeLegacySerialRows()
    {
        var configuration = TestDevicePluginConfiguration.Create();
        var result = await new LoadHardwareConfigHandler(configuration)
            .Handle(new LoadHardwareConfigQuery(), TestContext.Current.CancellationToken);

        var plc = Assert.Single(result.NetworkDevices);
        Assert.Equal("AP-PLC-01", plc.PlcCode);
        Assert.Empty(result.SerialDevices);
        Assert.Equal(0, configuration.SnapshotReadCount);
    }

    [Fact]
    public async Task SaveNetworkDevices_WritesOnlyThroughVersionedPluginStore()
    {
        var configuration = TestDevicePluginConfiguration.Create();
        var handler = new SaveNetworkDevicesHandler(configuration, [configuration]);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
            [
                new NetworkDeviceDto(
                    DevicePluginProjectionIds.Plc("AP-PLC-01"),
                    "PLC Changed",
                    DeviceType.PLC,
                    "Mc",
                    "10.0.0.8",
                    6001,
                    null,
                    null,
                    null,
                    4000,
                    true,
                    "updated",
                    "E4",
                    "AP-PLC-01")
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, configuration.WriteCount);
        Assert.Equal(2, configuration.GetRequiredSnapshot().ConfigurationVersion);
        Assert.Equal("10.0.0.8", configuration.GetRequiredSnapshot().Plcs.Single().IpAddress);
    }

    [Fact]
    public async Task SaveNetworkDevices_RejectsNonPlcWithoutWriting()
    {
        var configuration = TestDevicePluginConfiguration.Create();
        var handler = new SaveNetworkDevicesHandler(configuration, [configuration]);

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
            [
                new NetworkDeviceDto(
                    0,
                    "serial",
                    DeviceType.Scanner,
                    null,
                    "127.0.0.1",
                    1,
                    null,
                    null,
                    null,
                    1000,
                    true,
                    null,
                    null,
                    "SERIAL-01")
            ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("PLUGIN_PLC_CONFIGURATION_INVALID", result.ErrorMessage);
        Assert.Equal(0, configuration.WriteCount);
    }

    [Fact]
    public async Task SaveIoMappings_DeletesRemovedRowsAndDoesNotRecreateThem()
    {
        var configuration = TestDevicePluginConfiguration.Create(
            ioPoints:
            [
                Io("Signal.Keep", "D100"),
                Io("Signal.Delete", "D101")
            ]);
        var handler = new SaveIoMappingsHandler(configuration, [configuration]);
        var plcId = DevicePluginProjectionIds.Plc("AP-PLC-01");

        var result = await handler.Handle(
            new SaveIoMappingsCommand(
                plcId,
                [new IoMappingDto(
                    DevicePluginProjectionIds.Io("AP-PLC-01", "Signal.Keep"),
                    plcId,
                    "Signal.Keep",
                    "D100",
                    1,
                    "Int16",
                    "Read",
                    "单点读数据",
                    "status",
                    1,
                    null)]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.DoesNotContain(
            configuration.GetRequiredSnapshot().IoPoints,
            item => item.SignalKey == "Signal.Delete");
        Assert.Single(configuration.GetRequiredSnapshot().IoPoints);
    }

    [Fact]
    public async Task SaveIoMappings_RejectsUnknownPlcBeforeStoreWrite()
    {
        var configuration = TestDevicePluginConfiguration.Create();
        var result = await new SaveIoMappingsHandler(configuration, [configuration])
            .Handle(
                new SaveIoMappingsCommand(999, []),
                TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("PLUGIN_PLC_NOT_FOUND", result.ErrorMessage);
        Assert.Equal(0, configuration.WriteCount);
    }

    private static DevicePluginIoPointConfiguration Io(string key, string address)
        => new(
            "AP-PLC-01",
            key,
            address,
            1,
            "Int16",
            "Read",
            "单点读数据",
            "status",
            1,
            null);
}
