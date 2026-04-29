using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Homogenization.Config.Hardware;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationHardwareProfileBehaviorTests
{
    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldKeepTemplateMetadataInPluginProfile()
    {
        var provider = new HomogenizationHardwareProfileProvider();

        var defaults = provider.GetDefaultPlcSettings();
        var template = provider.GetDefaultIoTemplate();
        var trayCode = Assert.Single(template, x => x.Label == "Homogenization.TrayCode");

        Assert.Equal("Mc", defaults.DeviceModel);
        Assert.Equal(3000, defaults.ConnectTimeout);
        Assert.Equal(6000, defaults.Port1);
        Assert.Equal(string.Empty, trayCode.PlcAddress);
        Assert.Equal(30, trayCode.AddressCount);
        Assert.Equal("Ascii", trayCode.DataType);
        Assert.Equal("连续读数据", trayCode.Category);
        Assert.Equal("托盘数据", trayCode.GroupName);
        Assert.Equal("托盘码", trayCode.DisplayRole);
        Assert.Equal("匀浆模块 - 托盘码", trayCode.Remark);
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldRequireCategory()
    {
        var provider = new HomogenizationHardwareProfileProvider();
        var mappings = CreateValidSnapshots(provider)
            .Select(static mapping => mapping.Label == "Homogenization.TrayCode"
                ? mapping with { Category = string.Empty }
                : mapping)
            .ToArray();

        var validation = provider.ValidatePlcConfiguration("Mixer-PLC", "Mc", mappings);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldAcceptCompleteTemplate()
    {
        var provider = new HomogenizationHardwareProfileProvider();

        var validation = provider.ValidatePlcConfiguration("Mixer-PLC", "Mc", CreateValidSnapshots(provider));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues.Select(static x => x.Message)));
    }

    private static ModuleIoSnapshot[] CreateValidSnapshots(HomogenizationHardwareProfileProvider provider)
        => provider.GetDefaultIoTemplate()
            .Select(static template => new ModuleIoSnapshot(
                template.Label,
                $"D{template.SortOrder * 10}",
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.SortOrder,
                template.Category,
                template.GroupName,
                template.DisplayRole))
            .ToArray();
}
