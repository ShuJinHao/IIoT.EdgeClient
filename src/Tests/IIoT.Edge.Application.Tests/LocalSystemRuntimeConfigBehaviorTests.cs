using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;

namespace IIoT.Edge.Application.Tests;

public sealed class LocalSystemRuntimeConfigBehaviorTests
{
    [Fact]
    public async Task EnsureInitializedAsync_WhenMesRoleEnabled_ShouldBuildRuntimeSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        parameterConfigService.SystemConfigs.Add(new LocalSystemConfigSnapshot(
            1,
            CloudApiConfigParamSchema.Enabled,
            "true",
            null,
            1));
        var roleProvider = new MutableModuleParamRoleProvider { MesEnabled = true };
        var registry = new FakeProcessIntegrationRegistry(["TestPlugin"]);
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            roleProvider,
            registry,
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Current.MesUploadEnabled);
        Assert.True(service.Current.SystemCloudEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.OnlineHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.CloudSyncInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.RuntimeHeartbeatInterval);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenNoMesModuleRegistered_ShouldFailClosed()
    {
        var service = new LocalSystemRuntimeConfigService(
            new MutableLocalParameterConfigService(),
            new MutableModuleParamRoleProvider { MesEnabled = true },
            new FakeProcessIntegrationRegistry([]),
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.False(service.Current.MesUploadEnabled);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenNoCloudUploaderRegistered_ShouldStillUseSystemCloudSwitch()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        parameterConfigService.SystemConfigs.Add(new LocalSystemConfigSnapshot(
            1,
            CloudApiConfigParamSchema.Enabled,
            "true",
            null,
            1));
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            new MutableModuleParamRoleProvider(),
            new FakeProcessIntegrationRegistry([]),
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.True(service.Current.SystemCloudEnabled);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenCloudApiEnabledFalse_ShouldDisableSystemCloud()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        parameterConfigService.SystemConfigs.Add(new LocalSystemConfigSnapshot(
            1,
            "CloudApi:Enabled",
            "false",
            null,
            1));
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            new MutableModuleParamRoleProvider { MesEnabled = true },
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.False(service.Current.SystemCloudEnabled);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenRuntimeHeartbeatIntervalConfigured_ShouldUseIndependentInterval()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        parameterConfigService.SystemConfigs.Add(new LocalSystemConfigSnapshot(
            1,
            LocalSystemRuntimeConfigService.RuntimeHeartbeatIntervalSecondsKey,
            "15",
            null,
            1));
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            new MutableModuleParamRoleProvider { MesEnabled = true },
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(15), service.Current.RuntimeHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.OnlineHeartbeatInterval);
    }

    [Fact]
    public async Task ParameterConfigChanged_WhenModuleParamsChange_ShouldRefreshCurrentSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        var roleProvider = new MutableModuleParamRoleProvider { MesEnabled = false };
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            roleProvider,
            new FakeProcessIntegrationRegistry(["TestPlugin"]),
            new FakeLogService());

        await service.EnsureInitializedAsync(TestContext.Current.CancellationToken);
        Assert.False(service.Current.MesUploadEnabled);

        roleProvider.MesEnabled = true;
        parameterConfigService.NotifyModuleChanged();
        await WaitForAsync(() => service.Current.MesUploadEnabled);

        Assert.True(service.Current.MesUploadEnabled);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        static async Task ObserveAsync(Func<bool> observation, CancellationToken cancellationToken)
        {
            while (!observation())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        await ObserveAsync(predicate, TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    private sealed class MutableLocalParameterConfigService : ILocalParameterConfigService
    {
        public List<LocalSystemConfigSnapshot> SystemConfigs { get; } = [];

        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>(SystemConfigs);

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

        public void NotifyModuleChanged()
            => ParameterConfigChanged?.Invoke(
                this,
                new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.Module));
    }

    private sealed class MutableModuleParamRoleProvider : IModuleParamRoleProvider
    {
        public bool MesEnabled { get; set; }

        public Task<ModuleParamRoleValue?> GetAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ModuleParamRoleValue?>(null);

        public Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModuleParamRoleValue>>([]);

        public Task<string?> GetStringAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            string? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(defaultValue);

        public Task<string?> FirstStringAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<bool> GetBoolAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role == ModuleParamRole.MesEnabled ? MesEnabled : defaultValue);

        public Task<bool> AnyBoolAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult((moduleIds?.Count ?? 0) > 0
                && role == ModuleParamRole.MesEnabled
                && MesEnabled);
    }

    private sealed class FakeProcessIntegrationRegistry(
        IEnumerable<string> mesProcessTypes,
        IEnumerable<string>? cloudProcessTypes = null) : IProcessIntegrationRegistry
    {
        private readonly Dictionary<string, ProcessUploaderRegistration> _cloudUploaders = (cloudProcessTypes ?? [])
            .ToDictionary(
                static processType => processType,
                static processType => new ProcessUploaderRegistration(processType, ProcessUploadMode.Batch),
                StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ProcessUploaderRegistration> _mesUploaders = mesProcessTypes
            .ToDictionary(
                static processType => processType,
                static processType => new ProcessUploaderRegistration(processType, ProcessUploadMode.Single),
                StringComparer.OrdinalIgnoreCase);

        public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
            => throw new NotSupportedException();

        public void RegisterMesUploader(string processType, ProcessUploadMode uploadMode)
            => throw new NotSupportedException();

        public bool HasCloudUploader(string processType) => _cloudUploaders.ContainsKey(processType);

        public bool HasMesUploader(string processType) => _mesUploaders.ContainsKey(processType);

        public bool TryGetCloudUploader(string processType, out ProcessUploaderRegistration registration)
            => _cloudUploaders.TryGetValue(processType, out registration!);

        public bool TryGetMesUploader(string processType, out ProcessUploaderRegistration registration)
            => _mesUploaders.TryGetValue(processType, out registration!);

        public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetCloudUploaders()
            => _cloudUploaders;

        public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetMesUploaders()
            => _mesUploaders;
    }
}
