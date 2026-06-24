using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Caching.Memory;
using HomogenizationBusinessParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Business;
using HomogenizationCloudParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Cloud;
using HomogenizationMesParam = IIoT.Edge.Module.Homogenization.Config.Parameters.HomogenizationParams.Mes;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ModuleParameterBehaviorTests
{
    [Fact]
    public void ModuleParamRegistry_WhenHomogenizationRegistered_ShouldExposeThreeCategories()
    {
        var registry = CreateHomogenizationRegistry();

        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "Homogenization"
            && x.Name == nameof(HomogenizationMesParam.启用)
            && x.ValueKind == ParamValueKind.Bool);
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "Homogenization"
            && x.Name == nameof(HomogenizationMesParam.MesHealthPath)
            && x.Role == ModuleParamRole.MesHealthPath
            && x.DefaultValue == "/heath");
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "Homogenization"
            && x.Name == nameof(HomogenizationMesParam.OrderPath)
            && x.DefaultValue == "/dev/dev/get/order");
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "Homogenization"
            && x.Name == nameof(HomogenizationMesParam.BatchNumberPath)
            && x.DefaultValue == "/dev/dev/get/batchNumber");
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Cloud), x =>
            x.Name == nameof(HomogenizationCloudParam.启用)
            && x.Role == ModuleParamRole.CloudEnabled);
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Business), x =>
            x.Name == nameof(HomogenizationBusinessParam.启用托盘码重码验证));
    }

    [Fact]
    public void ModuleParamRegistry_WhenHomogenizationRegistered_ShouldKeepInternalNameAndExposeDisplayResources()
    {
        var registry = CreateHomogenizationRegistry();

        var mesEnabled = registry.GetDescriptors(ModuleParamCategory.Mes)
            .Single(x => x.ModuleId == "Homogenization" && x.Name == nameof(HomogenizationMesParam.启用));
        var cloudEnabled = registry.GetDescriptors(ModuleParamCategory.Cloud)
            .Single(x => x.ModuleId == "Homogenization" && x.Name == nameof(HomogenizationCloudParam.启用));

        Assert.Equal("启用", mesEnabled.Name);
        Assert.Equal("Module:Homogenization:Mes:启用", mesEnabled.StorageKey);
        Assert.Equal("Homogenization_Param_MesEnabled_DisplayName", mesEnabled.DisplayNameResourceKey);
        Assert.Equal("MES上传启用", mesEnabled.DisplayNameFallback);
        Assert.Equal("Homogenization_Param_MesEnabled_Description", mesEnabled.DescriptionResourceKey);

        Assert.Equal("启用", cloudEnabled.Name);
        Assert.Equal("Module:Homogenization:Cloud:启用", cloudEnabled.StorageKey);
        Assert.Equal("Homogenization_Param_CloudEnabled_DisplayName", cloudEnabled.DisplayNameResourceKey);
        Assert.Equal("云端上传启用", cloudEnabled.DisplayNameFallback);
        Assert.Equal("Homogenization_Param_CloudEnabled_Description", cloudEnabled.DescriptionResourceKey);
    }

    [Fact]
    public async Task LoadParamViewHandler_WhenHomogenizationRegistered_ShouldExposeDisplayFallbackWithoutChangingIdentity()
    {
        var registry = CreateHomogenizationRegistry();
        var handler = new LoadParamViewHandler(
            new CountingLocalParameterConfigService([]),
            registry,
            [new StubEdgeProcessModule()],
            new StubCloudApiConfigSnapshotProvider());

        var result = await handler.Handle(new LoadParamViewQuery(), CancellationToken.None);

        var mesEnabled = Assert.Single(result.MesParamGroups).Params
            .Single(x => x.Name == nameof(HomogenizationMesParam.启用));
        var cloudGroup = Assert.Single(result.CloudParamGroups);
        var cloudParams = cloudGroup.Params;
        var cloudEnabled = cloudParams
            .Single(x => x.Name == nameof(HomogenizationCloudParam.启用));

        Assert.Equal("Module:Homogenization:Mes:启用", mesEnabled.Key);
        Assert.Equal("启用", mesEnabled.Name);
        Assert.Equal("MES上传启用", mesEnabled.DisplayNameFallback);
        Assert.Contains("不调用 MES", mesEnabled.DescriptionFallback, StringComparison.Ordinal);

        Assert.Equal(CloudApiConfigParamSchema.ModuleId, cloudGroup.ModuleId);
        Assert.Equal("Navigation_Tab_CloudParams", cloudGroup.ModuleDisplayNameResourceKey);
        Assert.Equal("Module:Homogenization:Cloud:启用", cloudEnabled.Key);
        Assert.Equal("启用", cloudEnabled.Name);
        Assert.Equal("云端上传启用", cloudEnabled.DisplayNameFallback);
        Assert.Contains("不访问 Cloud", cloudEnabled.DescriptionFallback, StringComparison.Ordinal);
        Assert.Equal(
            Enum.GetNames<HomogenizationCloudParam>().Length + 1,
            cloudParams.Count);
        Assert.Contains(cloudParams, x =>
            x.Key == CloudApiConfigParamSchema.BaseUrl
            && x.Value == "https://config-cloud.test");
        Assert.DoesNotContain(cloudParams, x => x.Key == CloudApiConfigParamSchema.ProcessUploadPath);
        Assert.DoesNotContain(cloudParams, x => x.Key == CloudApiConfigParamSchema.PassStationBatchTemplatePath);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenNoDatabaseRows_ShouldReturnEnumDefaultsAndCacheSnapshot()
    {
        var registry = CreateHomogenizationRegistry();
        var config = new CountingLocalParameterConfigService([]);
        var provider = CreateProvider(registry, config);

        var first = await provider.GetAsync();
        var second = await provider.GetAsync();

        Assert.True(first.Mes<bool>(HomogenizationMesParam.启用));
        Assert.Equal("/heath", first.Mes<string>(HomogenizationMesParam.MesHealthPath));
        Assert.Equal("/dev/dev/get/order", first.Mes<string>(HomogenizationMesParam.OrderPath));
        Assert.Equal("/dev/dev/get/batchNumber", first.Mes<string>(HomogenizationMesParam.BatchNumberPath));
        Assert.False(first.Cloud<bool>(HomogenizationCloudParam.启用));
        Assert.False(first.Business<bool>(HomogenizationBusinessParam.启用托盘码重码验证));
        Assert.Equal(first.ModuleId, second.ModuleId);
        Assert.Equal(1, config.SystemQueryCount);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenDatabaseValueExists_ShouldUseConfiguredValue()
    {
        var registry = CreateHomogenizationRegistry();
        var config = new CountingLocalParameterConfigService(
        [
            new LocalSystemConfigSnapshot(
                1,
                ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Mes, nameof(HomogenizationMesParam.服务地址)),
                "https://mes.local",
                null,
                1),
            new LocalSystemConfigSnapshot(
                2,
                ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Business, nameof(HomogenizationBusinessParam.启用托盘码重码验证)),
                "true",
                null,
                2)
        ]);
        var provider = CreateProvider(registry, config);

        var snapshot = await provider.GetAsync();

        Assert.Equal("https://mes.local", snapshot.Mes<string>(HomogenizationMesParam.服务地址));
        Assert.True(snapshot.Business<bool>(HomogenizationBusinessParam.启用托盘码重码验证));
    }

    [Fact]
    public async Task ModuleParamProvider_WhenGenericTypeDoesNotMatchDeclaration_ShouldFailFast()
    {
        var provider = CreateProvider(
            CreateHomogenizationRegistry(),
            new CountingLocalParameterConfigService([]));
        var snapshot = await provider.GetAsync();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            snapshot.Mes<bool>(HomogenizationMesParam.服务地址));

        Assert.Contains("插件参数类型不匹配", ex.Message);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenDatabaseValueInvalid_ShouldFallbackToDefaultAndLogWarning()
    {
        var logger = new FakeLogService();
        var provider = CreateProvider(
            CreateHomogenizationRegistry(),
            new CountingLocalParameterConfigService(
            [
                new LocalSystemConfigSnapshot(
                    1,
                    ModuleParamKeys.StorageKey("Homogenization", ModuleParamCategory.Mes, nameof(HomogenizationMesParam.启用)),
                    "not-bool",
                    null,
                    1)
            ]),
            logger);

        var snapshot = await provider.GetAsync();

        Assert.True(snapshot.Mes<bool>(HomogenizationMesParam.启用));
        Assert.Contains(logger.Entries, entry =>
            entry.Level == "Warn"
            && entry.Message.Contains("无法转换为 Boolean", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ModuleParamProvider_WhenEnumsAreNotRegistered_ShouldFailFast()
    {
        var provider = CreateProvider(
            new ModuleParamRegistry(),
            new CountingLocalParameterConfigService([]));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetAsync());

        Assert.Contains("插件参数枚举未注册", ex.Message);
    }

    private static ModuleParamProvider<HomogenizationMesParam, HomogenizationCloudParam, HomogenizationBusinessParam> CreateProvider(
        ModuleParamRegistry registry,
        ILocalParameterConfigService config,
        FakeLogService? logger = null)
        => new(
            registry,
            new ModuleParamValueSnapshotLoader(config, new EdgeMemoryCacheService()),
            logger ?? new FakeLogService());

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

    private sealed class CountingLocalParameterConfigService(
        IReadOnlyList<LocalSystemConfigSnapshot> systemConfigs) : ILocalParameterConfigService
    {
        public int SystemQueryCount { get; private set; }

        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
        {
            SystemQueryCount++;
            return Task.FromResult(systemConfigs);
        }

        public Task InsertSystemConfigAsync(
            string key,
            string value,
            string? description = null,
            int sortOrder = 0,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteSystemConfigAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

    }

    private sealed class StubEdgeProcessModule : IEdgeProcessModule
    {
        public string ModuleId => "Homogenization";

        public string ProcessType => "Homogenization";

        public string DisplayName => "匀浆";

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
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
