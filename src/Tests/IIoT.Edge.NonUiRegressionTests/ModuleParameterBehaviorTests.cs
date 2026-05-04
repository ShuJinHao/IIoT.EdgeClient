using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Caching.Memory;
using HomogenizationBusinessParam = IIoT.Edge.Module.Homogenization.Config.Parameters.BusinessParam;
using HomogenizationCloudParam = IIoT.Edge.Module.Homogenization.Config.Parameters.CloudParam;
using HomogenizationMesParam = IIoT.Edge.Module.Homogenization.Config.Parameters.MesParam;

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
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Cloud), x =>
            x.Name == nameof(HomogenizationCloudParam.启用)
            && x.Role == ModuleParamRole.CloudEnabled);
        Assert.Contains(registry.GetDescriptors(ModuleParamCategory.Business), x =>
            x.Name == nameof(HomogenizationBusinessParam.启用托盘码重码验证));
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
            config,
            new EdgeMemoryCacheService(),
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

    }
}
