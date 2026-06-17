using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Infrastructure.Integration.Config;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.NonUiRegressionTests;

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
                new LocalSystemConfigSnapshot(3, CloudApiConfigParamSchema.ProcessUploadPath, "/local/process", null, 3),
                new LocalSystemConfigSnapshot(4, CloudApiConfigParamSchema.PassStationBatchTemplatePath, "/local/pass-stations/{typeKey}/batch", null, 4),
                new LocalSystemConfigSnapshot(5, CloudApiConfigParamSchema.RecipeByDeviceTemplatePath, "/local/recipes/{deviceId}", null, 5)
            ]));

        Assert.Equal("https://local-cloud.test/api/ping", provider.BuildUrl("/api/ping"));
        Assert.Equal("LOCAL-CLIENT", provider.GetClientCode());
        Assert.Equal("/local/process", provider.GetProcessUploadPath());
        Assert.Equal("/local/pass-stations/homogenization/batch", provider.GetPassStationBatchPath("Homogenization"));
        Assert.Equal($"/local/recipes/{deviceId}", provider.BuildRecipeByDevicePath(deviceId));
    }

    [Fact]
    public void CloudApiEndpointProvider_WhenCloudParamMissing_ShouldFallbackToAppSettings()
    {
        var provider = new CloudApiEndpointProvider(
            new TestOptionsMonitor<CloudApiConfig>(CreateConfig()),
            new FakeLocalParameterConfigService([]));

        Assert.Equal("https://config-cloud.test/api/ping", provider.BuildUrl("/api/ping"));
        Assert.Equal("CONFIG-CLIENT", provider.GetClientCode());
        Assert.Equal("/config/process", provider.GetProcessUploadPath());
        Assert.Equal("/config/pass-stations/homogenization/batch", provider.GetPassStationBatchPath("Homogenization"));
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
                DeviceLog = "/config/logs",
                ProcessUpload = "/config/process",
                PassStationBatchTemplate = "/config/pass-stations/{typeKey}/batch",
                CapacityHourly = "/config/capacity-hourly",
                CapacitySummary = "/config/capacity-summary",
                CapacitySummaryRange = "/config/capacity-range",
                RecipeByDeviceTemplate = "/config/recipes/{deviceId}"
            }
        };

    private sealed class FakeLocalParameterConfigService(
        IReadOnlyList<LocalSystemConfigSnapshot> systemConfigs) : ILocalParameterConfigService
    {
        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(systemConfigs);

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

    private sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = currentValue;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
