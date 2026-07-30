using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Presentation.Panels;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell.UiTests;

public sealed class DeviceSelectionContextBehaviorTests
{
    [Fact]
    public void PanelRegistration_ShouldExposeSameWritableAndReadOnlySelectionInstance()
    {
        var services = new ServiceCollection();
        services.AddPanelPresentation();
        using var provider = services.BuildServiceProvider();

        var writable = provider.GetRequiredService<IDeviceSelectionService>();
        var readOnly = provider.GetRequiredService<IDeviceSelectionContext>();

        Assert.Same(writable, readOnly);
        Assert.True(readOnly.IsAllSelected);
        writable.SelectDevice("正极模切07");
        Assert.Equal("正极模切07", readOnly.SelectedDeviceKey);
        Assert.False(readOnly.IsAllSelected);
    }

    [Fact]
    public void StableIdentityMapping_ShouldKeepPublicSelectionKeyAsRealDeviceName()
    {
        var selection = new DeviceSelectionService();
        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("当前显示名称", "P1-AP01")
        ]);

        selection.SelectDevice("当前显示名称");

        Assert.Equal("当前显示名称", selection.SelectedDeviceKey);
        Assert.Equal("P1-AP01", selection.SelectedPlcCode);
    }

    [Fact]
    public void StableIdentityMapping_WhenPlcCodeIsDuplicated_ShouldFailClosed()
    {
        var selection = new DeviceSelectionService();
        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("一号机", "PLC-DUP"),
            new PlcDeviceSelectionIdentity("二号机", "PLC-DUP")
        ]);

        selection.SelectDevice("一号机");

        Assert.Equal("一号机", selection.SelectedDeviceKey);
        Assert.Null(selection.SelectedPlcCode);
    }
}
