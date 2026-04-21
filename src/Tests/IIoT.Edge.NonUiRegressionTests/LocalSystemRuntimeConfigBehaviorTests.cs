using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class LocalSystemRuntimeConfigBehaviorTests
{
    [Fact]
    public async Task EnsureInitializedAsync_WhenSystemConfigsAreValid_ShouldBuildTypedSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService
        {
            SystemConfigs =
            [
                new LocalSystemConfigSnapshot(1, SystemConfigKey.MES服务地址.ToString(), "https://mes.local", null, 1),
                new LocalSystemConfigSnapshot(2, SystemConfigKey.启用MES上报.ToString(), "false", null, 2),
                new LocalSystemConfigSnapshot(3, SystemConfigKey.心跳间隔.ToString(), "15", null, 3),
                new LocalSystemConfigSnapshot(4, SystemConfigKey.云端同步周期.ToString(), "45", null, 4)
            ]
        };
        var service = new LocalSystemRuntimeConfigService(parameterConfigService, new FakeLogService());

        await service.EnsureInitializedAsync();

        Assert.Equal("https://mes.local", service.Current.MesBaseUrl);
        Assert.False(service.Current.MesUploadEnabled);
        Assert.Equal(TimeSpan.FromSeconds(15), service.Current.OnlineHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(45), service.Current.CloudSyncInterval);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenSystemConfigsAreInvalid_ShouldFallbackToDefaults()
    {
        var parameterConfigService = new MutableLocalParameterConfigService
        {
            SystemConfigs =
            [
                new LocalSystemConfigSnapshot(1, SystemConfigKey.MES服务地址.ToString(), "not-a-url", null, 1),
                new LocalSystemConfigSnapshot(2, SystemConfigKey.启用MES上报.ToString(), "bad-bool", null, 2),
                new LocalSystemConfigSnapshot(3, SystemConfigKey.心跳间隔.ToString(), "0", null, 3),
                new LocalSystemConfigSnapshot(4, SystemConfigKey.云端同步周期.ToString(), "-5", null, 4)
            ]
        };
        var service = new LocalSystemRuntimeConfigService(parameterConfigService, new FakeLogService());

        await service.EnsureInitializedAsync();

        Assert.Null(service.Current.MesBaseUrl);
        Assert.True(service.Current.MesUploadEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.OnlineHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.CloudSyncInterval);
    }

    [Fact]
    public async Task ParameterConfigChanged_WhenSystemConfigChanges_ShouldRefreshCurrentSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService
        {
            SystemConfigs =
            [
                new LocalSystemConfigSnapshot(1, SystemConfigKey.心跳间隔.ToString(), "30", null, 1)
            ]
        };
        var service = new LocalSystemRuntimeConfigService(parameterConfigService, new FakeLogService());

        await service.EnsureInitializedAsync();

        parameterConfigService.SystemConfigs =
        [
            new LocalSystemConfigSnapshot(1, SystemConfigKey.心跳间隔.ToString(), "12", null, 1),
            new LocalSystemConfigSnapshot(2, SystemConfigKey.云端同步周期.ToString(), "18", null, 2)
        ];

        parameterConfigService.NotifySystemChanged();
        await WaitForAsync(() => service.Current.OnlineHeartbeatInterval == TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.FromSeconds(12), service.Current.OnlineHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(18), service.Current.CloudSyncInterval);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

    private sealed class MutableLocalParameterConfigService : ILocalParameterConfigService
    {
        public IReadOnlyList<LocalSystemConfigSnapshot> SystemConfigs { get; set; } = [];

        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(SystemConfigs);

        public Task<string?> GetSystemConfigValueAsync(
            SystemConfigKey key,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                SystemConfigs.FirstOrDefault(x => string.Equals(x.Key, key.ToString(), StringComparison.OrdinalIgnoreCase))?.Value);

        public Task<IReadOnlyList<LocalDeviceParameterSnapshot>> GetDeviceParamsAsync(
            int deviceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalDeviceParameterSnapshot>>([]);

        public Task<string?> GetDeviceParamValueAsync(
            int deviceId,
            DeviceParamKey key,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public void NotifySystemChanged()
            => ParameterConfigChanged?.Invoke(
                this,
                new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.System));
    }
}
