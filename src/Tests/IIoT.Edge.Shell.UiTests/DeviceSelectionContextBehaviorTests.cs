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
}
