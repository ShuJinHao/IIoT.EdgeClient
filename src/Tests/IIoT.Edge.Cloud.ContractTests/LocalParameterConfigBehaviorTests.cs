using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class LocalParameterConfigBehaviorTests
{
    [Fact]
    public async Task LocalParameterConfigService_ReadsOnlyCurrentPluginSnapshot()
    {
        var configuration = TestPluginConfiguration.Create(
            settings:
            [
                new DevicePluginModuleSetting("Module:AP:Business:Speed", "120", "速度", "mm/s", 2),
                new DevicePluginModuleSetting("Module:AP:Mes:Enabled", "true", "MES", null, 1)
            ]);
        var service = new LocalParameterConfigService(configuration, [configuration]);

        var snapshots = await service.GetSystemConfigsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("Module:AP:Mes:Enabled", snapshots[0].Key);
        Assert.Equal("Module:AP:Business:Speed", snapshots[1].Key);
        Assert.DoesNotContain(snapshots, item => item.Key.StartsWith("CloudApi:", StringComparison.Ordinal));
        Assert.Equal(0, configuration.WriteCount);
    }

    [Fact]
    public async Task LocalParameterConfigService_InsertUsesPluginVersionedTransaction()
    {
        var configuration = TestPluginConfiguration.Create(
            settings:
            [
                new DevicePluginModuleSetting("Module:AP:Business:Existing", "keep", null, null, 1)
            ]);
        var service = new LocalParameterConfigService(configuration, [configuration]);
        var eventCount = 0;
        service.ParameterConfigChanged += (_, _) => eventCount++;

        await service.InsertSystemConfigAsync(
            "Module:AP:Business:Speed",
            "120",
            "速度",
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, configuration.WriteCount);
        Assert.Equal(2, configuration.GetRequiredSnapshot().ConfigurationVersion);
        Assert.Contains(configuration.GetRequiredSnapshot().ModuleSettings, item =>
            item.Key == "Module:AP:Business:Existing" && item.Value == "keep");
        Assert.Contains(configuration.GetRequiredSnapshot().ModuleSettings, item =>
            item.Key == "Module:AP:Business:Speed" && item.Value == "120");
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task SaveModuleParamsHandler_WritesOneAtomicSnapshotAndPreservesHiddenSettings()
    {
        var configuration = TestPluginConfiguration.Create(
            settings:
            [
                new DevicePluginModuleSetting("Module:AP:Mes:SignToken", "secret-reference", "签章引用", null, 1),
                new DevicePluginModuleSetting("Module:AP:Business:Speed", "80", "速度", "mm/s", 2)
            ]);
        var publisher = new RecordingChangePublisher();
        var handler = new SaveModuleParamsHandler(configuration, [configuration], publisher);

        var result = await handler.Handle(
            new SaveModuleParamsCommand(
            [
                new ModuleParamDto("Module:AP:Business:Speed", "120", "速度")
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(1, configuration.WriteCount);
        Assert.Equal(1, publisher.NotificationCount);
        Assert.Contains(configuration.GetRequiredSnapshot().ModuleSettings, item =>
            item.Key == "Module:AP:Mes:SignToken" && item.Value == "secret-reference");
        Assert.Contains(configuration.GetRequiredSnapshot().ModuleSettings, item =>
            item.Key == "Module:AP:Business:Speed" && item.Value == "120");
    }

    [Fact]
    public async Task SaveModuleParamsHandler_WhenVersionConflicts_FailsClosed()
    {
        var configuration = TestPluginConfiguration.Create();
        configuration.NextFailureReasonCode = "AP_CONFIGURATION_VERSION_CONFLICT";
        var handler = new SaveModuleParamsHandler(
            configuration,
            [configuration],
            new RecordingChangePublisher());

        var result = await handler.Handle(
            new SaveModuleParamsCommand(
            [
                new ModuleParamDto("Module:AP:Business:Speed", "120")
            ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("AP_CONFIGURATION_VERSION_CONFLICT", result.ErrorMessage);
        Assert.Equal(0, configuration.WriteCount);
    }

    [Fact]
    public void CloudApiParamView_OnlyAllowsBindingOwnedEnableProjection()
    {
        Assert.True(CloudApiConfigParamSchema.IsParamViewEditableKey(CloudApiConfigParamSchema.Enabled));
        Assert.False(CloudApiConfigParamSchema.IsParamViewEditableKey(CloudApiConfigParamSchema.BaseUrl));
        Assert.False(CloudApiConfigParamSchema.IsParamViewEditableKey(
            CloudApiConfigParamSchema.PassStationBatchTemplatePath));
        Assert.False(CloudApiConfigParamSchema.IsParamViewEditableKey(CloudApiConfigParamSchema.BootstrapSecret));
    }

    [Fact]
    public async Task SaveCloudApiConfigParamsHandler_RejectsRouteOverrideWithoutWritingProjection()
    {
        var projection = new RecordingProjectionWriter();
        var handler = new SaveCloudApiConfigParamsHandler(
            new RecordingChangePublisher(),
            new RecordingRuntimeConfigService(),
            projection);

        var result = await handler.Handle(
            new SaveCloudApiConfigParamsCommand(
            [
                new CloudApiConfigParamDto(
                    CloudApiConfigParamSchema.PassStationBatchTemplatePath,
                    "/not-binding-owned/{typeKey}")
            ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("BINDING_CLOUD_CONFIGURATION_READ_ONLY", result.ErrorMessage);
        Assert.Empty(projection.Values);
    }

    [Fact]
    public async Task SaveCloudApiConfigParamsHandler_EnableWritesOnlyProjectionAndRefreshesRuntime()
    {
        var projection = new RecordingProjectionWriter();
        var runtime = new RecordingRuntimeConfigService();
        var publisher = new RecordingChangePublisher();
        var handler = new SaveCloudApiConfigParamsHandler(publisher, runtime, projection);

        var result = await handler.Handle(
            new SaveCloudApiConfigParamsCommand(
            [
                new CloudApiConfigParamDto(CloudApiConfigParamSchema.Enabled, "true")
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal([true], projection.Values);
        Assert.Equal(1, runtime.RefreshCount);
        Assert.Equal(1, publisher.NotificationCount);
    }

    private sealed class RecordingChangePublisher : ILocalParameterConfigChangePublisher
    {
        public int NotificationCount { get; private set; }

        public void NotifyModuleChanged() => NotificationCount++;
    }

    private sealed class RecordingRuntimeConfigService : ILocalSystemRuntimeConfigService
    {
        public int RefreshCount { get; private set; }

        public SystemRuntimeConfigSnapshot Current { get; private set; } = SystemRuntimeConfigSnapshot.Default;

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => RefreshAsync(cancellationToken);

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProjectionWriter : ICloudProfileSwitchProjectionWriter
    {
        public List<bool> Values { get; } = [];

        public Task WriteAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values.Add(enabled);
            return Task.CompletedTask;
        }
    }

    private sealed class TestPluginConfiguration(DevicePluginConfigurationSnapshot initial)
        : IDevicePluginConfigurationSnapshotAccessor,
          IDevicePluginConfigurationStoreV1
    {
        private DevicePluginConfigurationSnapshot _snapshot = initial;

        public event EventHandler<DevicePluginConfigurationChangedEventArgs>? ConfigurationChanged;

        public bool IsInitialized => true;

        public int WriteCount { get; private set; }

        public string? NextFailureReasonCode { get; set; }

        public DevicePluginConfigurationSnapshot GetRequiredSnapshot() => _snapshot;

        public IReadOnlyList<DevicePluginPlcSnapshot> GetPlcs() => [];

        public IReadOnlyList<DevicePluginIoPointSnapshot> GetIoPoints() => [];

        public IReadOnlyList<DevicePluginTaskBindingSnapshot> GetTaskBindings() => [];

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<DevicePluginConfigurationSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_snapshot);
        }

        public Task<DevicePluginConfigurationWriteResult> UpdateModuleSettingsAsync(
            IReadOnlyList<DevicePluginModuleSetting> settings,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NextFailureReasonCode is { } failure)
            {
                NextFailureReasonCode = null;
                return Task.FromResult(new DevicePluginConfigurationWriteResult(
                    DevicePluginConfigurationWriteStatus.VersionConflict,
                    _snapshot.ConfigurationVersion,
                    failure));
            }

            if (expectedConfigurationVersion != _snapshot.ConfigurationVersion)
            {
                return Task.FromResult(new DevicePluginConfigurationWriteResult(
                    DevicePluginConfigurationWriteStatus.VersionConflict,
                    _snapshot.ConfigurationVersion,
                    "PLUGIN_CONFIGURATION_VERSION_CONFLICT"));
            }

            var previous = _snapshot.ConfigurationVersion;
            _snapshot = _snapshot with
            {
                ConfigurationVersion = previous + 1,
                ModuleSettings = settings.ToArray(),
                CapturedAtUtc = DateTimeOffset.UtcNow
            };
            WriteCount++;
            ConfigurationChanged?.Invoke(
                this,
                new DevicePluginConfigurationChangedEventArgs(previous, _snapshot.ConfigurationVersion));
            return Task.FromResult(new DevicePluginConfigurationWriteResult(
                DevicePluginConfigurationWriteStatus.Applied,
                _snapshot.ConfigurationVersion));
        }

        public Task<DevicePluginConfigurationWriteResult> UpsertPlcAsync(
            DevicePluginPlcConfiguration configuration,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Unsupported();

        public Task<DevicePluginConfigurationWriteResult> DeletePlcAsync(
            string plcCode,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Unsupported();

        public Task<DevicePluginConfigurationWriteResult> UpsertIoPointAsync(
            DevicePluginIoPointConfiguration configuration,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Unsupported();

        public Task<DevicePluginConfigurationWriteResult> DeleteIoPointAsync(
            string plcCode,
            string signalKey,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Unsupported();

        public Task<DevicePluginConfigurationWriteResult> ReplaceTaskBindingsAsync(
            string plcCode,
            IReadOnlyList<DevicePluginTaskBindingConfiguration> bindings,
            long expectedConfigurationVersion,
            CancellationToken cancellationToken = default)
            => Unsupported();

        public static TestPluginConfiguration Create(
            IReadOnlyList<DevicePluginModuleSetting>? settings = null)
            => new(new DevicePluginConfigurationSnapshot(
                new DevicePluginIdentity("CLIENT-TEST", "AP", "AP"),
                1,
                [],
                [],
                [],
                settings ?? [],
                DateTimeOffset.UtcNow));

        private Task<DevicePluginConfigurationWriteResult> Unsupported()
            => Task.FromResult(new DevicePluginConfigurationWriteResult(
                DevicePluginConfigurationWriteStatus.Rejected,
                _snapshot.ConfigurationVersion,
                "UNSUPPORTED"));
    }
}
