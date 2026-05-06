using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Runtime.Signals;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationHardwareProfileBehaviorTests
{
    [Fact]
    public void HomogenizationPlcSignalProfile_ShouldBuildTemplateFromBusinessGroups()
    {
        var groupedSignals = HomogenizationSignalTestProfile.Group("IO 交互")
            .Concat(HomogenizationSignalTestProfile.Group("设备状态/报警"))
            .Concat(HomogenizationSignalTestProfile.Group("托盘信息"))
            .Concat(HomogenizationSignalTestProfile.Group("实时数据"))
            .Concat(HomogenizationSignalTestProfile.Group("配方数据"))
            .Concat(HomogenizationSignalTestProfile.Group("出料数据"))
            .OrderBy(static signal => signal.SortOrder)
            .ToArray();

        Assert.Equal(
            groupedSignals.Select(static signal => signal.SignalKey),
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SignalKey));
        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Count,
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SignalKey).Distinct().Count());
        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Count,
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SortOrder).Distinct().Count());
        Assert.Contains(HomogenizationSignalTestProfile.Signals, static signal => signal.Direction == ModuleSignalDirection.Read);
        Assert.Contains(HomogenizationSignalTestProfile.Signals, static signal => signal.Direction == ModuleSignalDirection.Write);
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("IO 交互"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationSignal.进站触发));
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("IO 交互"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationSignal.进站应答));
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("设备状态/报警"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationSignal.设备状态值));

        var provider = new HomogenizationHardwareProfileProvider(new HomogenizationPlcSignalProfile());

        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SignalKey),
            provider.GetDefaultIoTemplate().Select(static mapping => mapping.SignalKey));
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldKeepTemplateMetadataInPluginProfile()
    {
        var provider = new HomogenizationHardwareProfileProvider(new HomogenizationPlcSignalProfile());

        var defaults = provider.GetDefaultPlcSettings();
        var template = provider.GetDefaultIoTemplate();
        var trayCode = Assert.Single(template, x => x.SignalKey == "Homogenization.TrayCode");

        Assert.Equal("Mc", defaults.DeviceModel);
        Assert.Equal(3000, defaults.ConnectTimeout);
        Assert.Equal(6000, defaults.Port1);
        Assert.Equal(HomogenizationSignalTestProfile.Get(HomogenizationSignal.托盘码).DefaultAddress, trayCode.PlcAddress);
        Assert.Equal(30, trayCode.AddressCount);
        Assert.Equal("Ascii", trayCode.DataType);
        Assert.Equal("连续读数据", trayCode.Category);
        Assert.Equal("托盘数据", trayCode.BusinessGroup);
        Assert.Equal("托盘码", trayCode.SignalName);
        Assert.Equal("匀浆模块 - 托盘码", trayCode.Remark);
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldRequireCategory()
    {
        var provider = new HomogenizationHardwareProfileProvider(new HomogenizationPlcSignalProfile());
        var mappings = CreateValidSnapshots(provider)
            .Select(static mapping => mapping.SignalKey == "Homogenization.TrayCode"
                ? mapping with { Category = string.Empty }
                : mapping)
            .ToArray();

        var validation = provider.ValidatePlcConfiguration("Mixer-PLC", "Mc", mappings);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldAcceptCompleteTemplate()
    {
        var provider = new HomogenizationHardwareProfileProvider(new HomogenizationPlcSignalProfile());

        var validation = provider.ValidatePlcConfiguration("Mixer-PLC", "Mc", CreateValidSnapshots(provider));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues.Select(static x => x.Message)));
    }

    [Fact]
    public void BufferLogicalSignalAccessor_ShouldThrowChineseErrorForMissingMapping()
    {
        var accessor = CreateAccessor([]);

        var exception = Assert.Throws<InvalidOperationException>(() => accessor.ReadUInt16(HomogenizationSignal.进站触发));

        Assert.Contains("未绑定 Read IO 映射", exception.Message);
    }

    [Fact]
    public void BufferLogicalSignalAccessor_ShouldThrowChineseErrorForDataTypeMismatch()
    {
        var binding = HomogenizationSignalTestProfile.Get(HomogenizationSignal.托盘码);
        var accessor = CreateAccessor(
            [
                new(
                    binding.SignalKey,
                    binding.DefaultAddress,
                    binding.AddressCount,
                    "Int16",
                    binding.DirectionText,
                    binding.SortOrder,
                    binding.Category,
                    binding.BusinessGroup,
                    binding.SignalName)
            ]);

        var exception = Assert.Throws<InvalidOperationException>(() => accessor.ReadAscii(HomogenizationSignal.托盘码));

        Assert.Contains("数据类型不匹配", exception.Message);
    }

    private static ModuleIoSnapshot[] CreateValidSnapshots(HomogenizationHardwareProfileProvider provider)
        => provider.GetDefaultIoTemplate()
            .Select(static template => new ModuleIoSnapshot(
                template.SignalKey,
                $"D{template.SortOrder * 10}",
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.SortOrder,
                template.Category,
                template.BusinessGroup,
                template.SignalName))
            .ToArray();

    private static BufferLogicalSignalAccessor<HomogenizationSignal> CreateAccessor(IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        var buffer = new TestPlcBuffer();
        return new BufferLogicalSignalAccessor<HomogenizationSignal>(
            buffer,
            bindings,
            new HomogenizationPlcSignalProfile());
    }

    private sealed class TestPlcBuffer : IPlcBuffer
    {
        public ushort GetReadValue(int index) => 0;

        public void SetWriteValue(int index, ushort value)
        {
        }
    }
}
