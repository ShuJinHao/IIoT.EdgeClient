using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Application.Tests;

internal sealed class TestDevicePluginConfiguration(
    DevicePluginConfigurationSnapshot initial)
    : IDevicePluginConfigurationSnapshotAccessor,
      IDevicePluginConfigurationStoreV1
{
    private DevicePluginConfigurationSnapshot _snapshot = initial;

    public event EventHandler<DevicePluginConfigurationChangedEventArgs>? ConfigurationChanged;

    public bool IsInitialized => true;

    public int SnapshotReadCount { get; private set; }

    public int WriteCount { get; private set; }

    public DevicePluginConfigurationSnapshot GetRequiredSnapshot() => _snapshot;

    public IReadOnlyList<DevicePluginPlcSnapshot> GetPlcs()
        => _snapshot.Plcs.Select(item => new DevicePluginPlcSnapshot(
            DevicePluginProjectionIds.Plc(item.PlcCode),
            item)).ToArray();

    public IReadOnlyList<DevicePluginIoPointSnapshot> GetIoPoints()
        => _snapshot.IoPoints.Select(item => new DevicePluginIoPointSnapshot(
            DevicePluginProjectionIds.Io(item.PlcCode, item.SignalKey),
            DevicePluginProjectionIds.Plc(item.PlcCode),
            item)).ToArray();

    public IReadOnlyList<DevicePluginTaskBindingSnapshot> GetTaskBindings()
        => _snapshot.TaskBindings.Select(item => new DevicePluginTaskBindingSnapshot(
            DevicePluginProjectionIds.Binding(item.PlcCode, item.TaskKey),
            DevicePluginProjectionIds.Plc(item.PlcCode),
            item)).ToArray();

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<DevicePluginConfigurationSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotReadCount++;
        return Task.FromResult(_snapshot);
    }

    public Task<DevicePluginConfigurationWriteResult> UpsertPlcAsync(
        DevicePluginPlcConfiguration configuration,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            Plcs = snapshot.Plcs
                .Where(item => !string.Equals(item.PlcCode, configuration.PlcCode, StringComparison.OrdinalIgnoreCase))
                .Append(configuration)
                .ToArray()
        }, cancellationToken);

    public Task<DevicePluginConfigurationWriteResult> DeletePlcAsync(
        string plcCode,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            Plcs = snapshot.Plcs.Where(item => !Same(item.PlcCode, plcCode)).ToArray(),
            IoPoints = snapshot.IoPoints.Where(item => !Same(item.PlcCode, plcCode)).ToArray(),
            TaskBindings = snapshot.TaskBindings.Where(item => !Same(item.PlcCode, plcCode)).ToArray()
        }, cancellationToken);

    public Task<DevicePluginConfigurationWriteResult> UpsertIoPointAsync(
        DevicePluginIoPointConfiguration configuration,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            IoPoints = snapshot.IoPoints
                .Where(item => !Same(item.PlcCode, configuration.PlcCode)
                               || !Same(item.SignalKey, configuration.SignalKey))
                .Append(configuration)
                .ToArray()
        }, cancellationToken);

    public Task<DevicePluginConfigurationWriteResult> DeleteIoPointAsync(
        string plcCode,
        string signalKey,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            IoPoints = snapshot.IoPoints
                .Where(item => !Same(item.PlcCode, plcCode) || !Same(item.SignalKey, signalKey))
                .ToArray()
        }, cancellationToken);

    public Task<DevicePluginConfigurationWriteResult> ReplaceTaskBindingsAsync(
        string plcCode,
        IReadOnlyList<DevicePluginTaskBindingConfiguration> bindings,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            TaskBindings = snapshot.TaskBindings
                .Where(item => !Same(item.PlcCode, plcCode))
                .Concat(bindings)
                .ToArray()
        }, cancellationToken);

    public Task<DevicePluginConfigurationWriteResult> UpdateModuleSettingsAsync(
        IReadOnlyList<DevicePluginModuleSetting> settings,
        long expectedConfigurationVersion,
        CancellationToken cancellationToken = default)
        => WriteAsync(expectedConfigurationVersion, snapshot => snapshot with
        {
            ModuleSettings = settings.ToArray()
        }, cancellationToken);

    public static TestDevicePluginConfiguration Create(
        IReadOnlyList<DevicePluginPlcConfiguration>? plcs = null,
        IReadOnlyList<DevicePluginIoPointConfiguration>? ioPoints = null,
        IReadOnlyList<DevicePluginTaskBindingConfiguration>? bindings = null,
        IReadOnlyList<DevicePluginModuleSetting>? settings = null,
        long version = 1)
        => new(new DevicePluginConfigurationSnapshot(
            new DevicePluginIdentity("CLIENT-TEST", "AP", "AP"),
            version,
            plcs ?? [DefaultPlc()],
            ioPoints ?? [],
            bindings ?? [],
            settings ?? [],
            DateTimeOffset.UtcNow));

    public static DevicePluginPlcConfiguration DefaultPlc(string code = "AP-PLC-01")
        => new(
            code,
            "PLC 1",
            "PLC",
            "Mc",
            "E3",
            "127.0.0.1",
            6000,
            null,
            3000,
            true,
            null);

    private Task<DevicePluginConfigurationWriteResult> WriteAsync(
        long expectedVersion,
        Func<DevicePluginConfigurationSnapshot, DevicePluginConfigurationSnapshot> mutate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedVersion != _snapshot.ConfigurationVersion)
        {
            return Task.FromResult(new DevicePluginConfigurationWriteResult(
                DevicePluginConfigurationWriteStatus.VersionConflict,
                _snapshot.ConfigurationVersion,
                "PLUGIN_CONFIGURATION_VERSION_CONFLICT"));
        }

        var previous = _snapshot.ConfigurationVersion;
        _snapshot = mutate(_snapshot) with
        {
            ConfigurationVersion = previous + 1,
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

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
