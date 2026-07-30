using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Application.Common.Identity;
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
        var stable = provider.GetRequiredService<IPlcDeviceSelectionContext>();

        Assert.Same(writable, readOnly);
        Assert.Same(writable, stable);
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

    [Fact]
    public void StableIdentityMapping_WhenDeviceIsRenamed_ShouldKeepStableSelectionAndVerifiedAliases()
    {
        var selection = new DeviceSelectionService();
        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("旧名称", "P1-AP01")
        ]);
        selection.SelectDevice("旧名称");

        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("新名称", "P1-AP01")
        ]);

        Assert.Equal("旧名称", selection.SelectedDeviceKey);
        Assert.Equal("P1-AP01", selection.SelectedPlcCode);
        Assert.Equal(
            ["新名称", "旧名称"],
            selection.SelectedDeviceNameAliases);
    }

    [Fact]
    public void StableIdentityMapping_WhenSelectedPlcDisappearsAndNameIsReused_ShouldKeepPhantomStableSelection()
    {
        var selection = new DeviceSelectionService();
        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("旧名称", "P1-AP01")
        ]);
        selection.SelectDevice("旧名称");

        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("旧名称", "P1-AP02")
        ]);

        Assert.Equal("旧名称", selection.SelectedDeviceKey);
        Assert.Equal("P1-AP01", selection.SelectedPlcCode);
        Assert.Empty(selection.SelectedDeviceNameAliases);
    }

    [Fact]
    public void StableIdentityMapping_ShouldLoadPersistedVerifiedAliases()
    {
        var aliases = new InMemoryPlcIdentityAliasRegistry();
        aliases.ObserveVerifiedAlias("P1-AP01", "历史名称");
        var selection = new DeviceSelectionService(aliases);

        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("当前名称", "P1-AP01")
        ]);
        selection.SelectDevice("当前名称");

        Assert.Equal(
            ["当前名称", "历史名称"],
            selection.SelectedDeviceNameAliases);
    }

    [Fact]
    public void StableIdentityMapping_WhenAliasEqualsAnotherCurrentPlcCode_ShouldExcludeAlias()
    {
        var selection = new DeviceSelectionService();
        selection.UpdatePlcIdentities(
        [
            new PlcDeviceSelectionIdentity("一号机", "PLC-A"),
            new PlcDeviceSelectionIdentity("PLC-A", "PLC-B")
        ]);

        selection.SelectDevice("PLC-A");

        Assert.Equal("PLC-B", selection.SelectedPlcCode);
        Assert.Empty(selection.SelectedDeviceNameAliases);
    }
}
