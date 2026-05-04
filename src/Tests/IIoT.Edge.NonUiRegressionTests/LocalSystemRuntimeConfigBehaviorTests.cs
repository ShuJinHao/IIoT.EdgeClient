using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class LocalSystemRuntimeConfigBehaviorTests
{
    [Fact]
    public async Task EnsureInitializedAsync_WhenMesRoleEnabled_ShouldBuildRuntimeSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        var roleProvider = new MutableModuleParamRoleProvider { MesEnabled = true };
        var registry = new FakeProcessIntegrationRegistry(["Homogenization"]);
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            roleProvider,
            registry,
            new FakeLogService());

        await service.EnsureInitializedAsync();

        Assert.True(service.Current.MesUploadEnabled);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.OnlineHeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), service.Current.CloudSyncInterval);
    }

    [Fact]
    public async Task EnsureInitializedAsync_WhenNoMesModuleRegistered_ShouldFailClosed()
    {
        var service = new LocalSystemRuntimeConfigService(
            new MutableLocalParameterConfigService(),
            new MutableModuleParamRoleProvider { MesEnabled = true },
            new FakeProcessIntegrationRegistry([]),
            new FakeLogService());

        await service.EnsureInitializedAsync();

        Assert.False(service.Current.MesUploadEnabled);
    }

    [Fact]
    public async Task ParameterConfigChanged_WhenModuleParamsChange_ShouldRefreshCurrentSnapshot()
    {
        var parameterConfigService = new MutableLocalParameterConfigService();
        var roleProvider = new MutableModuleParamRoleProvider { MesEnabled = false };
        var service = new LocalSystemRuntimeConfigService(
            parameterConfigService,
            roleProvider,
            new FakeProcessIntegrationRegistry(["Homogenization"]),
            new FakeLogService());

        await service.EnsureInitializedAsync();
        Assert.False(service.Current.MesUploadEnabled);

        roleProvider.MesEnabled = true;
        parameterConfigService.NotifyModuleChanged();
        await WaitForAsync(() => service.Current.MesUploadEnabled);

        Assert.True(service.Current.MesUploadEnabled);
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

            await Task.Yield();
        }

        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

    private sealed class MutableLocalParameterConfigService : ILocalParameterConfigService
    {
        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>([]);

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
            => Task.FromResult(MesEnabled);

        public Task<bool> AnyBoolAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult((moduleIds?.Count ?? 0) > 0 && MesEnabled);
    }

    private sealed class FakeProcessIntegrationRegistry(IEnumerable<string> mesProcessTypes) : IProcessIntegrationRegistry
    {
        private readonly Dictionary<string, MesUploaderRegistration> _mesUploaders = mesProcessTypes
            .ToDictionary(
                static processType => processType,
                static processType => new MesUploaderRegistration(processType, MesUploadMode.Single),
                StringComparer.OrdinalIgnoreCase);

        public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
            => throw new NotSupportedException();

        public void RegisterMesUploader(string processType, MesUploadMode uploadMode)
            => throw new NotSupportedException();

        public bool HasCloudUploader(string processType) => false;

        public bool HasMesUploader(string processType) => _mesUploaders.ContainsKey(processType);

        public bool TryGetCloudUploader(string processType, out CloudUploaderRegistration registration)
        {
            registration = default!;
            return false;
        }

        public bool TryGetMesUploader(string processType, out MesUploaderRegistration registration)
            => _mesUploaders.TryGetValue(processType, out registration!);

        public IReadOnlyDictionary<string, CloudUploaderRegistration> GetCloudUploaders()
            => new Dictionary<string, CloudUploaderRegistration>();

        public IReadOnlyDictionary<string, MesUploaderRegistration> GetMesUploaders()
            => _mesUploaders;
    }
}
