using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Common.Caching.Memory;

namespace IIoT.Edge.Application.Tests;

public sealed class ModuleParameterBehaviorTests
{
    [Fact]
    public void ModuleParamRegistry_WhenTestModuleRegistered_ShouldExposeThreeCategories()
    {
        var registry = CreateTestModuleRegistry();

        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "TestModule"
            && x.Name == nameof(TestMesParam.启用)
            && x.ValueKind == ParamValueKind.Bool);
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "TestModule"
            && x.Name == nameof(TestMesParam.MesHealthPath)
            && x.Role == ModuleParamRole.MesHealthPath
            && x.DefaultValue == "/heath");
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "TestModule"
            && x.Name == nameof(TestMesParam.OrderPath)
            && x.DefaultValue == "/dev/dev/get/order");
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Mes), x =>
            x.ModuleId == "TestModule"
            && x.Name == nameof(TestMesParam.BatchNumberPath)
            && x.DefaultValue == "/dev/dev/get/batchNumber");
        Assert.Empty(registry.GetDescriptors(ModuleParamCategory.Cloud));
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Business), x =>
            x.Name == nameof(TestBusinessParam.启用托盘码重码验证));
    }

    [Fact]
    public void ModuleParamRegistry_WhenTestModuleRegistered_ShouldKeepInternalNameAndExposeDisplayResources()
    {
        var registry = CreateTestModuleRegistry();

        var mesEnabled = registry.GetDescriptors(ModuleParamCategory.Mes)
            .Single(x => x.ModuleId == "TestModule" && x.Name == nameof(TestMesParam.启用));

        Assert.Equal("启用", mesEnabled.Name);
        Assert.Equal("Module:TestModule:Mes:启用", mesEnabled.StorageKey);
        Assert.Equal("TestModule_Param_MesEnabled_DisplayName", mesEnabled.DisplayNameResourceKey);
        Assert.Equal("MES上传启用", mesEnabled.DisplayNameFallback);
        Assert.Equal("TestModule_Param_MesEnabled_Description", mesEnabled.DescriptionResourceKey);

        Assert.Empty(registry.GetDescriptors(ModuleParamCategory.Cloud));
    }

    [Fact]
    public async Task LoadParamViewHandler_WhenTestModuleRegistered_ShouldExposeDisplayFallbackWithoutChangingIdentity()
    {
        var registry = CreateTestModuleRegistry();
        var handler = new LoadParamViewHandler(
            new CountingLocalParameterConfigService([]),
            registry,
            [new StubEdgeProcessModule()],
            new StubCloudApiConfigSnapshotProvider());

        var result = await handler.Handle(new LoadParamViewQuery(), TestContext.Current.CancellationToken);

        var mesEnabled = Assert.Single(result.MesParamGroups).Params
            .Single(x => x.Name == nameof(TestMesParam.启用));
        var cloudGroup = Assert.Single(result.CloudParamGroups);
        var cloudParams = cloudGroup.Params;
        var cloudEnabled = cloudParams
            .Single(x => x.Key == CloudApiConfigParamSchema.Enabled);

        Assert.Equal("Module:TestModule:Mes:启用", mesEnabled.Key);
        Assert.Equal("启用", mesEnabled.Name);
        Assert.Equal("MES上传启用", mesEnabled.DisplayNameFallback);
        Assert.Contains("不调用 MES", mesEnabled.DescriptionFallback, StringComparison.Ordinal);

        Assert.Equal(CloudApiConfigParamSchema.ModuleId, cloudGroup.ModuleId);
        Assert.Equal("Navigation_Tab_CloudParams", cloudGroup.ModuleDisplayNameResourceKey);
        Assert.Equal("Enabled", cloudEnabled.Name);
        Assert.Equal("云端启用", cloudEnabled.DisplayNameFallback);
        Assert.Contains("唯一总开关", cloudEnabled.DescriptionFallback, StringComparison.Ordinal);
        var expectedCloudApiParamCount = CloudApiConfigParamSchema.Descriptors
            .Count(static descriptor => CloudApiConfigParamSchema.IsParamViewEditableKey(descriptor.Key));
        Assert.Equal(
            expectedCloudApiParamCount,
            cloudParams.Count);
        Assert.Contains(cloudParams, x =>
            x.Key == CloudApiConfigParamSchema.BaseUrl
            && x.Value == "https://config-cloud.test");
        Assert.Contains(cloudParams, x =>
            x.Key == CloudApiConfigParamSchema.PassStationBatchTemplatePath
            && x.Value == "/config/pass-stations/{typeKey}/batch");
        Assert.DoesNotContain(cloudParams, x => x.Key == CloudApiConfigParamSchema.ClientCode);
        Assert.DoesNotContain(cloudParams, x => x.Key == CloudApiConfigParamSchema.BootstrapSecret);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenNoDatabaseRows_ShouldReturnEnumDefaultsAndCacheSnapshot()
    {
        var registry = CreateTestModuleRegistry();
        var config = new CountingLocalParameterConfigService([]);
        var provider = CreateProvider(registry, config);

        var first = await provider.GetAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(first.Mes<bool>(TestMesParam.启用));
        Assert.Equal("/heath", first.Mes<string>(TestMesParam.MesHealthPath));
        Assert.Equal("/dev/dev/get/order", first.Mes<string>(TestMesParam.OrderPath));
        Assert.Equal("/dev/dev/get/batchNumber", first.Mes<string>(TestMesParam.BatchNumberPath));
        Assert.False(first.Business<bool>(TestBusinessParam.启用托盘码重码验证));
        Assert.Equal(first.ModuleId, second.ModuleId);
        Assert.Equal(1, config.SystemQueryCount);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenDatabaseValueExists_ShouldUseConfiguredValue()
    {
        var registry = CreateTestModuleRegistry();
        var config = new CountingLocalParameterConfigService(
        [
            new LocalSystemConfigSnapshot(
                1,
                ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.服务地址)),
                "https://mes.local",
                null,
                1),
            new LocalSystemConfigSnapshot(
                2,
                ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Business, nameof(TestBusinessParam.启用托盘码重码验证)),
                "true",
                null,
                2)
        ]);
        var provider = CreateProvider(registry, config);

        var snapshot = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://mes.local", snapshot.Mes<string>(TestMesParam.服务地址));
        Assert.True(snapshot.Business<bool>(TestBusinessParam.启用托盘码重码验证));
    }

    [Fact]
    public async Task ModuleParamProvider_WhenGenericTypeDoesNotMatchDeclaration_ShouldFailFast()
    {
        var provider = CreateProvider(
            CreateTestModuleRegistry(),
            new CountingLocalParameterConfigService([]));
        var snapshot = await provider.GetAsync(TestContext.Current.CancellationToken);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            snapshot.Mes<bool>(TestMesParam.服务地址));

        Assert.Contains("插件参数类型不匹配", ex.Message);
    }

    [Fact]
    public async Task ModuleParamProvider_WhenDatabaseValueInvalid_ShouldFallbackToDefaultAndLogWarning()
    {
        var logger = new FakeLogService();
        var provider = CreateProvider(
            CreateTestModuleRegistry(),
            new CountingLocalParameterConfigService(
            [
                new LocalSystemConfigSnapshot(
                    1,
                    ModuleParamKeys.StorageKey("TestModule", ModuleParamCategory.Mes, nameof(TestMesParam.启用)),
                    "not-bool",
                    null,
                    1)
            ]),
            logger);

        var snapshot = await provider.GetAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.Mes<bool>(TestMesParam.启用));
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetAsync(TestContext.Current.CancellationToken));

        Assert.Contains("插件参数枚举未注册", ex.Message);
    }

    private static ModuleParamProvider<TestMesParam, TestCloudParam, TestBusinessParam> CreateProvider(
        ModuleParamRegistry registry,
        ILocalParameterConfigService config,
        FakeLogService? logger = null)
        => new(
            registry,
            new ModuleParamValueSnapshotLoader(config, new EdgeMemoryCacheService()),
            logger ?? new FakeLogService());

    private static ModuleParamRegistry CreateTestModuleRegistry()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            "TestModule",
            typeof(TestMesParam),
            typeof(TestCloudParam),
            typeof(TestBusinessParam));
        return registry;
    }

    private enum TestMesParam
    {
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "true",
            Role = ModuleParamRole.MesEnabled,
            DisplayNameResourceKey = "TestModule_Param_MesEnabled_DisplayName",
            DisplayNameFallback = "MES上传启用",
            DescriptionResourceKey = "TestModule_Param_MesEnabled_Description",
            DescriptionFallback = "关闭后不探测 MES 心跳、不调用 MES 业务接口。")]
        启用,

        [ModuleParam(ParamValueKind.String)]
        服务地址,

        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/heath",
            Role = ModuleParamRole.MesHealthPath)]
        MesHealthPath,

        [ModuleParam(ParamValueKind.String, DefaultValue = "/dev/dev/get/order")]
        OrderPath,

        [ModuleParam(ParamValueKind.String, DefaultValue = "/dev/dev/get/batchNumber")]
        BatchNumberPath
    }

    private enum TestCloudParam
    {
    }

    private enum TestBusinessParam
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        启用托盘码重码验证
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
        public string ModuleId => "TestModule";

        public string ProcessType => "TestModule";

        public string DisplayName => "测试模块";

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
                "/config/pass-stations/{typeKey}/batch",
                "/config/capacity-hourly",
                "/config/capacity-summary",
                "/config/capacity-range",
                "/config/recipes/{deviceId}",
                "/config/client-releases/device/{deviceId}/catalog",
                "/config/client-version-reports");
    }
}
