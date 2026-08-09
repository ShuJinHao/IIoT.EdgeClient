using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Sdk.Hardware;

namespace IIoT.Edge.Application.Tests;

public sealed class IoMappingSchemaReconciliationBehaviorTests
{
    [Fact]
    public async Task ReconcileAsync_UsesPluginStoreForInsertAndDelete()
    {
        var profile = new TestHardwareProfileProvider();
        var configuration = TestDevicePluginConfiguration.Create(
            ioPoints:
            [
                Io("TestModule.Interaction.Inbound", "D999", "Read", 2, "扫码进站 PLC 读点"),
                Io("Legacy.Signal", "D1", "Read", 900, null)
            ]);
        var resolver = new ModuleHardwareProfileResolver([profile]);
        var reconciler = new ConfigSchemaReconciler(
            [new IoMappingSchemaSource(configuration, resolver)],
            [new IoMappingConfigValueStore(configuration, [configuration], resolver)]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var items = configuration.GetRequiredSnapshot().IoPoints;
        Assert.Equal(profile.GetIoMappingCandidates().Count, items.Count);
        Assert.DoesNotContain(items, item => item.SignalKey == "Legacy.Signal");
        Assert.Contains(items, item =>
            item.SignalKey == "TestModule.Interaction.Inbound"
            && item.PlcAddress == "D999");
        Assert.True(configuration.WriteCount >= 2);
    }

    [Fact]
    public async Task ReconcileAsync_RepairsOnlyDeclaredLegacyRemarkAndIsIdempotent()
    {
        var profile = new TestHardwareProfileProvider();
        var legacy = profile.GetIoMappingCandidates().First(item => item.LegacyRemarks is { Count: > 0 });
        var manual = profile.GetIoMappingCandidates().First(item => item.SignalKey != legacy.SignalKey);
        var configuration = TestDevicePluginConfiguration.Create(
            ioPoints:
            [
                Io(legacy.SignalKey, "D999", legacy.Direction, legacy.SortOrder, legacy.LegacyRemarks![0]),
                Io(manual.SignalKey, "D998", manual.Direction, manual.SortOrder, "现场自定义备注")
            ]);
        var resolver = new ModuleHardwareProfileResolver([profile]);
        var reconciler = new ConfigSchemaReconciler(
            [new IoMappingSchemaSource(configuration, resolver)],
            [new IoMappingConfigValueStore(configuration, [configuration], resolver)]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);
        var writesAfterFirst = configuration.WriteCount;
        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var items = configuration.GetRequiredSnapshot().IoPoints;
        Assert.Equal(legacy.Remark, items.Single(item => item.SignalKey == legacy.SignalKey).Remark);
        Assert.Equal("现场自定义备注", items.Single(item => item.SignalKey == manual.SignalKey).Remark);
        Assert.Equal(writesAfterFirst, configuration.WriteCount);
    }

    private static DevicePluginIoPointConfiguration Io(
        string key,
        string address,
        string direction,
        int sortOrder,
        string? remark)
        => new(
            "AP-PLC-01",
            key,
            address,
            1,
            "Int16",
            direction,
            direction == "Write" ? "单点写数据" : "单点读数据",
            "测试",
            sortOrder,
            remark);

    private sealed class TestHardwareProfileProvider : IModuleHardwareProfileProvider
    {
        private static readonly IReadOnlyList<ModuleIoTemplateEntry> Templates =
        [
            new(
                "TestModule.Interaction.Inbound",
                "D701",
                1,
                "Int16",
                "Read",
                2,
                "扫码进站 PLC 读点",
                "单点读数据",
                "扫码进站",
                ["旧扫码进站"]),
            new(
                "TestModule.Status",
                "D710",
                1,
                "Int16",
                "Read",
                3,
                "设备状态",
                "单点读数据",
                "设备状态")
        ];

        public string ModuleId => "TestModule";

        public ModulePlcDefaults GetDefaultPlcSettings() => new("Mc", 3000, 6000);

        public PlcIoRuntimePolicy GetIoRuntimePolicy() => PlcIoRuntimePolicy.Default;

        public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate() => Templates;

        public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates() => Templates;

        public ModuleIoTemplateEntry ResolveIoTemplateForDevice(
            string deviceName,
            ModuleIoTemplateEntry template) => template;

        public ModuleHardwareValidationResult ValidatePlcConfiguration(
            string deviceName,
            string? deviceModel,
            IReadOnlyCollection<ModuleIoSnapshot> mappings)
            => ModuleHardwareValidationResult.Success();
    }
}
