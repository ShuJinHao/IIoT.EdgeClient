using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;

namespace IIoT.Edge.Application.Tests;

public sealed class ModuleParamSchemaReconciliationBehaviorTests
{
    [Fact]
    public async Task ReconcileAsync_WhenPluginCloudSchemaIsEmpty_ShouldDeleteLegacyPluginCloudKeysAndPreserveOtherCategories()
    {
        var registry = CreateTestModuleRegistry();
        var cloudEnabledKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Cloud, "启用");
        var staleCloudKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Cloud, "LegacyOnly");
        var mesKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.服务地址));
        var configService = new MutableLocalParameterConfigService(
        [
            new LocalSystemConfigSnapshot(1, cloudEnabledKey, "true", null, 1),
            new LocalSystemConfigSnapshot(2, staleCloudKey, "stale", null, 2),
            new LocalSystemConfigSnapshot(3, mesKey, "http://mes.local", null, 3)
        ]);
        var source = new ModuleParamSchemaSource(
            registry,
            ModuleParamCategory.Cloud,
            ModuleParamSchemaIds.Cloud);
        var store = new ModuleParamConfigValueStore(
            configService,
            ModuleParamCategory.Cloud,
            ModuleParamSchemaIds.Cloud);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var configs = await configService.GetSystemConfigsAsync(TestContext.Current.CancellationToken);
        var cloudKeys = configs
            .Where(static x => x.Key.Contains(":Cloud:", StringComparison.OrdinalIgnoreCase))
            .Select(static x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedCloudKeys = registry.GetDescriptors(ModuleParamCategory.Cloud)
            .Select(static x => x.StorageKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(expectedCloudKeys);
        Assert.Empty(cloudKeys);
        Assert.DoesNotContain(configs, x => x.Key == staleCloudKey);
        Assert.DoesNotContain(configs, x => x.Key == cloudEnabledKey);
        Assert.Equal("http://mes.local", configs.Single(x => x.Key == mesKey).Value);
    }

    [Fact]
    public async Task CloudApiConfigSchemaSource_WhenLoaded_ShouldUseAppSettingsSnapshotDefaults()
    {
        var source = new CloudApiConfigSchemaSource(new StubCloudApiConfigSnapshotProvider());

        var items = await source.GetItemsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(items, item =>
            item.Key == CloudApiConfigParamSchema.BaseUrl
            && item.DefaultValue == "https://config-cloud.test");
        Assert.Contains(items, item =>
            item.Key == CloudApiConfigParamSchema.RecipeByDeviceTemplatePath
            && item.DefaultValue == "/config/recipes/{deviceId}");
    }

    [Fact]
    public async Task ReconcileAsync_WhenExistingModuleParamMatchesLegacyDefault_ShouldRepairOnlyLegacyValue()
    {
        var registry = CreateTestModuleRegistry(
        [
            new ModuleParamDefaultOverride(
                ModuleParamCategory.Mes,
                nameof(TestMesParam.服务地址),
                "http://mes-current.example.test:8080",
                ["http://mes-legacy.example.test:8081"])
        ]);
        var mesKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.服务地址));
        var customUpperComputerNoKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.UpperComputerNo));
        var configService = new MutableLocalParameterConfigService(
        [
            new LocalSystemConfigSnapshot(1, mesKey, "http://mes-legacy.example.test:8081", null, 1),
            new LocalSystemConfigSnapshot(2, customUpperComputerNoKey, "CUSTOM-UC", null, 2)
        ]);
        var source = new ModuleParamSchemaSource(
            registry,
            ModuleParamCategory.Mes,
            ModuleParamSchemaIds.Mes);
        var store = new ModuleParamConfigValueStore(
            configService,
            ModuleParamCategory.Mes,
            ModuleParamSchemaIds.Mes);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var configs = await configService.GetSystemConfigsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("http://mes-current.example.test:8080", configs.Single(x => x.Key == mesKey).Value);
        Assert.Equal("CUSTOM-UC", configs.Single(x => x.Key == customUpperComputerNoKey).Value);
    }

    [Fact]
    public async Task ReconcileAsync_WhenPluginExplicitlyDeclaresBlankLegacyDefault_ShouldRepairBlankAndPreserveCustomValue()
    {
        var registry = CreateTestModuleRegistry(
        [
            new ModuleParamDefaultOverride(
                ModuleParamCategory.Mes,
                nameof(TestMesParam.服务地址),
                "http://mes-current.example.test:8080",
                [string.Empty])
        ]);
        var mesKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.服务地址));
        var customUpperComputerNoKey = ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.UpperComputerNo));
        var configService = new MutableLocalParameterConfigService(
        [
            new LocalSystemConfigSnapshot(1, mesKey, string.Empty, null, 1),
            new LocalSystemConfigSnapshot(2, customUpperComputerNoKey, "CUSTOM-UC", null, 2)
        ]);
        var source = new ModuleParamSchemaSource(
            registry,
            ModuleParamCategory.Mes,
            ModuleParamSchemaIds.Mes);
        var store = new ModuleParamConfigValueStore(
            configService,
            ModuleParamCategory.Mes,
            ModuleParamSchemaIds.Mes);
        var reconciler = new ConfigSchemaReconciler([source], [store]);

        await reconciler.ReconcileAsync(TestContext.Current.CancellationToken);

        var configs = await configService.GetSystemConfigsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("http://mes-current.example.test:8080", configs.Single(x => x.Key == mesKey).Value);
        Assert.Equal("CUSTOM-UC", configs.Single(x => x.Key == customUpperComputerNoKey).Value);
    }

    private static ModuleParamRegistry CreateTestModuleRegistry(
        IReadOnlyCollection<ModuleParamDefaultOverride>? defaultOverrides = null)
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            "TestModule",
            typeof(TestMesParam),
            typeof(TestCloudParam),
            typeof(TestBusinessParam),
            defaultOverrides);
        return registry;
    }

    private enum TestMesParam
    {
        [ModuleParam(ParamValueKind.String)]
        服务地址,

        [ModuleParam(ParamValueKind.String)]
        UpperComputerNo
    }

    private enum TestCloudParam
    {
    }

    private enum TestBusinessParam
    {
    }

    private sealed class MutableLocalParameterConfigService(
        IEnumerable<LocalSystemConfigSnapshot> systemConfigs) : ILocalParameterConfigService
    {
        private readonly List<LocalSystemConfigSnapshot> _systemConfigs = [.. systemConfigs];
        private int _nextId = systemConfigs.Any() ? systemConfigs.Max(static x => x.Id) + 1 : 1;

        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>([.. _systemConfigs]);

        public Task InsertSystemConfigAsync(
            string key,
            string value,
            string? description = null,
            int sortOrder = 0,
            CancellationToken cancellationToken = default)
        {
            _systemConfigs.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            _systemConfigs.Add(new LocalSystemConfigSnapshot(_nextId++, key, value, description, sortOrder));
            return Task.CompletedTask;
        }

        public Task DeleteSystemConfigAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _systemConfigs.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }
    }

    private sealed class StubCloudApiConfigSnapshotProvider : ICloudApiConfigSnapshotProvider
    {
        public CloudApiConfigSnapshot GetCurrent()
            => new(
                "https://config-cloud.test",
                "CONFIG-CLIENT",
                "secret",
                "/config/device-instance",
                "/config/bootstrap-refresh",
                "/config/login",
                "/config/human-refresh",
                "/config/logs",
                "/config/pass-stations/{typeKey}/batch",
                "/config/capacity-hourly",
                "/config/capacity-summary",
                "/config/capacity-range",
                "/config/recipes/{deviceId}",
                "/config/client-releases/device/{deviceId}/catalog",
                "/config/client-version-reports");
    }
}
