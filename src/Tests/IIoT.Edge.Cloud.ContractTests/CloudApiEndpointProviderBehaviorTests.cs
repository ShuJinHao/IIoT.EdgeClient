using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Infrastructure.Integration.Config;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class CloudApiEndpointProviderBehaviorTests
{
    [Fact]
    public void CloudApiEndpointProvider_WhenLocalConfigExists_ShouldPreferSystemConfigValue()
    {
        var deviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var provider = new CloudApiEndpointProvider(
            new TestOptionsMonitor<CloudApiConfig>(CreateConfig()),
            new FakeLocalParameterConfigService(
            [
                new LocalSystemConfigSnapshot(1, CloudApiConfigParamSchema.BaseUrl, "https://local-cloud.test", null, 1),
                new LocalSystemConfigSnapshot(2, CloudApiConfigParamSchema.ClientCode, "LOCAL-CLIENT", null, 2),
                new LocalSystemConfigSnapshot(3, CloudApiConfigParamSchema.PassStationBatchTemplatePath, "/local/pass-stations/{typeKey}/batch", null, 3),
                new LocalSystemConfigSnapshot(4, CloudApiConfigParamSchema.RecipeByDeviceTemplatePath, "/local/recipes/{deviceId}", null, 4),
                new LocalSystemConfigSnapshot(5, CloudApiConfigParamSchema.EdgeHostPlcRuntimeStatesPath, "/local/plc-runtime-states", null, 5)
            ]));

        Assert.Equal("https://local-cloud.test/api/ping", provider.BuildUrl("/api/ping"));
        Assert.Equal("LOCAL-CLIENT", provider.GetClientCode());
        Assert.Equal("/local/pass-stations/testplugin/batch", provider.GetPassStationBatchPath("TestPlugin"));
        Assert.Equal($"/local/recipes/{deviceId}", provider.BuildRecipeByDevicePath(deviceId));
        Assert.Equal("/local/plc-runtime-states", provider.GetEdgeHostPlcRuntimeStatesPath());
    }

    [Fact]
    public void CloudApiEndpointProvider_WhenCloudParamMissing_ShouldFallbackToAppSettings()
    {
        var provider = new CloudApiEndpointProvider(
            new TestOptionsMonitor<CloudApiConfig>(CreateConfig()),
            new FakeLocalParameterConfigService([]));

        Assert.Equal("https://config-cloud.test/api/ping", provider.BuildUrl("/api/ping"));
        Assert.Equal("CONFIG-CLIENT", provider.GetClientCode());
        Assert.Equal("/config/pass-stations/testplugin/batch", provider.GetPassStationBatchPath("TestPlugin"));
        Assert.Equal("/config/human-session", provider.GetHumanSessionValidationPath());
        Assert.Equal("/config/plc-runtime-states", provider.GetEdgeHostPlcRuntimeStatesPath());
    }

    private static CloudApiConfig CreateConfig()
        => new()
        {
            BaseUrl = "https://config-cloud.test",
            ClientCode = "CONFIG-CLIENT",
            BootstrapSecret = "secret",
            Paths = new CloudApiPaths
            {
                DeviceInstance = "/config/device-instance",
                BootstrapRefresh = "/config/bootstrap-refresh",
                IdentityDeviceLogin = "/config/login",
                HumanIdentityRefresh = "/config/human-refresh",
                HumanSessionValidation = "/config/human-session",
                DeviceLog = "/config/logs",
                PassStationBatchTemplate = "/config/pass-stations/{typeKey}/batch",
                CapacityHourly = "/config/capacity-hourly",
                CapacitySummary = "/config/capacity-summary",
                CapacitySummaryRange = "/config/capacity-range",
                RecipeByDeviceTemplate = "/config/recipes/{deviceId}",
                EdgeHostPlcRuntimeStates = "/config/plc-runtime-states"
            }
        };

    private sealed class FakeLocalParameterConfigService(
        IReadOnlyList<LocalSystemConfigSnapshot> systemConfigs) : ILocalSystemConfigSnapshotReader
    {
        public IReadOnlyList<LocalSystemConfigSnapshot> GetCurrentSystemConfigs() => systemConfigs;
    }

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
