using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Runtime.Signals;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationHardwareProfileBehaviorTests
{
    [Fact]
    public void HomogenizationPlcSignalProfiles_ShouldBuildTemplateFromBusinessGroups()
    {
        var groupedSignals = HomogenizationSignalTestProfile.Group("信号交互")
            .Concat(HomogenizationSignalTestProfile.Group("设备状态"))
            .Concat(HomogenizationSignalTestProfile.Group("托盘数据"))
            .Concat(HomogenizationSignalTestProfile.Group("实时数据"))
            .Concat(HomogenizationSignalTestProfile.Group("配方数组"))
            .Concat(HomogenizationSignalTestProfile.Group("出料数据"))
            .OrderBy(static signal => signal.SortOrder)
            .ToArray();

        Assert.Equal(
            groupedSignals.Select(static signal => signal.SignalKey),
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SignalKey));
        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Count,
            HomogenizationSignalTestProfile.Signals.Select(static signal => $"{signal.SignalKey}:{signal.DirectionText}").Distinct().Count());
        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Count,
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SortOrder).Distinct().Count());
        Assert.Contains(HomogenizationSignalTestProfile.Signals, static signal => signal.Direction == ModuleSignalDirection.Read);
        Assert.Contains(HomogenizationSignalTestProfile.Signals, static signal => signal.Direction == ModuleSignalDirection.Write);
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("信号交互"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)
                && signal.Direction == ModuleSignalDirection.Read);
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("信号交互"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.Interaction.扫码进站)
                && signal.Direction == ModuleSignalDirection.Write);
        Assert.Contains(
            HomogenizationSignalTestProfile.Group("设备状态"),
            signal => signal.SignalKey == HomogenizationSignalTestProfile.SignalKey(HomogenizationPlcSignals.SingleRead.设备状态值));

        var provider = new HomogenizationHardwareProfileProvider();

        Assert.Equal(
            HomogenizationSignalTestProfile.Signals.Select(static signal => signal.SignalKey),
            provider.GetDefaultIoTemplate().Select(static mapping => mapping.SignalKey));
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldKeepTemplateMetadataInPluginProfile()
    {
        var provider = new HomogenizationHardwareProfileProvider();

        var defaults = provider.GetDefaultPlcSettings();
        var template = provider.GetDefaultIoTemplate();
        var trayCode = Assert.Single(template, x => x.SignalKey == "Homogenization.TrayCode");

        Assert.Equal("Mc", defaults.DeviceModel);
        Assert.Equal(3000, defaults.ConnectTimeout);
        Assert.Equal(6000, defaults.Port1);
        Assert.Equal(HomogenizationSignalTestProfile.Get(HomogenizationPlcSignals.ContinuousRead.托盘码).DefaultAddress, trayCode.PlcAddress);
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
        var provider = new HomogenizationHardwareProfileProvider();
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
        var provider = new HomogenizationHardwareProfileProvider();

        var validation = provider.ValidatePlcConfiguration("Mixer-PLC", "Mc", CreateValidSnapshots(provider));

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues.Select(static x => x.Message)));
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldKeepInteractionGroupsPaired()
    {
        var provider = new HomogenizationHardwareProfileProvider();
        var template = provider.GetDefaultIoTemplate();

        Assert.Contains(template, static mapping => mapping.Category == "信号交互");
        Assert.Contains(template, static mapping => mapping.Category == "单点读数据");
        Assert.Contains(template, static mapping => mapping.Category == "连续读数据");
        Assert.DoesNotContain(template, static mapping => mapping.BusinessGroup == "test1");

        foreach (var group in template.Where(static mapping => mapping.Category == "信号交互").GroupBy(static mapping => mapping.BusinessGroup))
        {
            Assert.Contains(group, static mapping => mapping.Direction == "Read");
            Assert.Contains(group, static mapping => mapping.Direction == "Write");
        }
    }

    [Fact]
    public void HomogenizationHardwareProfileProvider_ShouldExposeEnumCandidatesWithoutSeedingNoAttributeSignals()
    {
        var provider = new HomogenizationHardwareProfileProvider();

        var defaults = provider.GetDefaultIoTemplate();
        var candidates = provider.GetIoMappingCandidates();

        Assert.DoesNotContain(defaults, static mapping => mapping.SignalKey == "Homogenization.Interaction.test1");

        var testCandidates = candidates
            .Where(static mapping => mapping.SignalKey == "Homogenization.Interaction.test1")
            .ToArray();
        Assert.Equal(2, testCandidates.Length);
        Assert.Contains(testCandidates, static mapping =>
            mapping.Direction == "Read"
            && mapping.Category == "信号交互"
            && mapping.BusinessGroup == "test1"
            && mapping.PlcAddress == string.Empty
            && mapping.AddressCount == 1);
        Assert.Contains(testCandidates, static mapping =>
            mapping.Direction == "Write"
            && mapping.Category == "信号交互"
            && mapping.BusinessGroup == "test1"
            && mapping.PlcAddress == string.Empty
            && mapping.AddressCount == 1);
    }

    [Fact]
    public void HomogenizationPlcSignalsSource_ShouldOnlyContainSignalEnums()
    {
        var root = FindRepositoryRoot();
        var signalFile = Path.Combine(
            root,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Config",
            "Hardware",
            "HomogenizationPlcSignals.cs");
        var text = File.ReadAllText(signalFile);

        Assert.Contains("public static class HomogenizationPlcSignals", text);
        Assert.Contains("public enum Interaction", text);
        Assert.Contains("public enum SingleRead", text);
        Assert.DoesNotContain("Attribute : Attribute", text);
        Assert.DoesNotContain("HomogenizationSignalMetadata", text);
        Assert.DoesNotContain("Profile", text);
    }

    [Fact]
    public void BufferLogicalSignalAccessor_ShouldThrowChineseErrorForMissingMapping()
    {
        var accessor = CreateInteractionAccessor([]);

        var exception = Assert.Throws<InvalidOperationException>(() => accessor.ReadUInt16(HomogenizationPlcSignals.Interaction.扫码进站));

        Assert.Contains("未绑定 Read IO 映射", exception.Message);
    }

    [Fact]
    public void BufferLogicalSignalAccessor_ShouldThrowChineseErrorForDataTypeMismatch()
    {
        var binding = HomogenizationSignalTestProfile.Get(HomogenizationPlcSignals.ContinuousRead.托盘码);
        var accessor = CreateContinuousReadAccessor(
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

        var exception = Assert.Throws<InvalidOperationException>(() => accessor.ReadAscii(HomogenizationPlcSignals.ContinuousRead.托盘码));

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

    private static BufferLogicalSignalAccessor<HomogenizationPlcSignals.Interaction> CreateInteractionAccessor(IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        var buffer = new TestPlcBuffer();
        return new BufferLogicalSignalAccessor<HomogenizationPlcSignals.Interaction>(
            buffer,
            bindings,
            new HomogenizationInteractionSignalProfile());
    }

    private static BufferLogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead> CreateContinuousReadAccessor(IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        var buffer = new TestPlcBuffer();
        return new BufferLogicalSignalAccessor<HomogenizationPlcSignals.ContinuousRead>(
            buffer,
            bindings,
            new HomogenizationContinuousReadSignalProfile());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("未找到 IIoT.EdgeClient.slnx。");
    }

    private sealed class TestPlcBuffer : IPlcBuffer
    {
        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

        public ushort GetReadValue(int index) => 0;

        public bool TryGetReadWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public bool TryGetWriteWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public void SetWriteValue(int index, ushort value)
        {
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(string.Empty, "Write"));
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, "Write"));
        }
    }
}



