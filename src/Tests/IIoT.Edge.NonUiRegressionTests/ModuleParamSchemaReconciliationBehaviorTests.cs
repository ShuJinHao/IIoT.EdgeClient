using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using HomogenizationBusinessParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Business;
using HomogenizationCloudParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Cloud;
using HomogenizationMesParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Mes;

namespace IIoT.Edge.NonUiRegressionTests;

    public sealed class ModuleParamSchemaReconciliationBehaviorTests
    {
    [Fact]
    public async Task ReconcileAsync_WhenCloudSchemaRegistered_ShouldSeedMissingDefaultsDeleteStaleAndPreserveExisting()
    {
        var registry = CreateHomogenizationRegistry();
        var cloudEnabledKey = ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Cloud, nameof(HomogenizationCloudParam.启用));
        var staleCloudKey = ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Cloud, "LegacyOnly");
        var mesKey = ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Mes, nameof(HomogenizationMesParam.服务地址));
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

        Assert.True(expectedCloudKeys.SetEquals(cloudKeys));
        Assert.DoesNotContain(configs, x => x.Key == staleCloudKey);
        Assert.Equal("true", configs.Single(x => x.Key == cloudEnabledKey).Value);
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

    private static ModuleParamRegistry CreateHomogenizationRegistry()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            "Homogenization",
            typeof(HomogenizationMesParam),
            typeof(HomogenizationCloudParam),
            typeof(HomogenizationBusinessParam));
        return registry;
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
                "/config/process",
                "/config/pass-stations/{typeKey}/batch",
                "/config/capacity-hourly",
                "/config/capacity-summary",
                "/config/capacity-range",
                "/config/recipes/{deviceId}",
                "/config/client-releases/device/{deviceId}/catalog",
                "/config/client-version-reports");
    }
}
