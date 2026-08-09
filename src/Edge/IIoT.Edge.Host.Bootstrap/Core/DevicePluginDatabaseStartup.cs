using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plugins;

namespace IIoT.Edge.Shell.Core;

public interface IDevicePluginDatabaseStartup
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class DevicePluginDatabaseStartupException : InvalidOperationException
{
    public DevicePluginDatabaseStartupException(string reasonCode, Exception? innerException = null)
        : base(reasonCode, innerException)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? "PLUGIN_DATABASE_STARTUP_FAILED"
            : reasonCode.Trim();
    }

    public string ReasonCode { get; }
}

/// <summary>
/// Formal v3 database cutover. The Host supplies only the current identity and isolated paths;
/// the plugin owns schema inspection, migration, adoption, seed and every database transaction.
/// </summary>
public sealed class DevicePluginDatabaseStartup(
    IDevicePluginRuntimeContext runtimeContext,
    IReadOnlyCollection<ModulePluginDescriptor> descriptors,
    IEnumerable<IDevicePluginDatabaseLifecycleV1> lifecycles,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores,
    DevicePluginConfigurationSnapshotCache snapshotCache,
    ILogService logger) : IDevicePluginDatabaseStartup
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var runtime = runtimeContext.Current;
        if (!runtime.IsV3)
        {
            return;
        }

        var descriptorMatches = descriptors
            .Where(descriptor => string.Equals(
                descriptor.ModuleId,
                runtime.ModuleId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (descriptorMatches.Length != 1
            || descriptorMatches[0].PrivateDatabaseContract is not { } contract)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_MANIFEST_OWNER_INVALID");
        }

        var lifecycleMatches = lifecycles.ToArray();
        var storeMatches = stores.ToArray();
        if (lifecycleMatches.Length != 1 || storeMatches.Length != 1)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var lifecycle = lifecycleMatches[0];
        var store = storeMatches[0];
        var ownerType = lifecycle.GetType();
        if (!ReferenceEquals(lifecycle, store)
            || !string.Equals(ownerType.FullName, contract.EntryPoint, StringComparison.Ordinal)
            || !string.Equals(
                ownerType.Assembly.GetName().Name,
                contract.MigrationAssembly,
                StringComparison.Ordinal))
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_PORT_OWNER_MISMATCH");
        }

        var descriptor = descriptorMatches[0];
        var pluginRoot = ResolvePluginRoot(descriptor.PluginDirectory, runtime.ClientCode);
        var databaseDirectory = Path.Combine(pluginRoot, "db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "plugin.db");
        var legacyDatabasePath = Path.Combine(databaseDirectory, "edge.db");
        var databaseExists = File.Exists(databasePath)
            && new FileInfo(databasePath).Length > 0;
        var request = new DevicePluginDatabaseLifecycleRequest(
            new DevicePluginIdentity(
                runtime.ClientCode,
                runtime.ModuleId,
                runtime.ProcessType),
            databasePath,
            File.Exists(legacyDatabasePath) ? legacyDatabasePath : null,
            ModulePluginHostRuntime.HostVersion,
            runtime.PluginVersion,
            !databaseExists);

        try
        {
            var inspection = await lifecycle
                .InspectAsync(request, cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccessful(inspection, contract.SchemaVersion, "PLUGIN_DATABASE_INSPECTION_REJECTED");

            var initialized = await lifecycle
                .InitializeOrMigrateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccessful(initialized, contract.SchemaVersion, "PLUGIN_DATABASE_CUTOVER_REJECTED");

            await snapshotCache.RefreshAsync(cancellationToken).ConfigureAwait(false);
            logger.Info(
                $"[PluginDatabase][ClientCode={runtime.ClientCode}][Module={runtime.ModuleId}] " +
                $"私有数据库已就绪，Schema={initialized.SchemaVersion}，" +
                $"SeedApplied={initialized.SeedApplied}。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DevicePluginDatabaseStartupException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_STARTUP_FAILED",
                exception);
        }
    }

    private static string ResolvePluginRoot(string pluginDirectory, string clientCode)
    {
        var appDirectory = Path.GetFullPath(pluginDirectory);
        if (!string.Equals(Path.GetFileName(appDirectory), "app", StringComparison.OrdinalIgnoreCase))
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_APP_DIRECTORY_INVALID");
        }

        var pluginRoot = Directory.GetParent(appDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(pluginRoot)
            || !string.Equals(
                Path.GetFileName(pluginRoot),
                DevicePluginIdentity.NormalizeClientCode(clientCode),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_CLIENT_DIRECTORY_MISMATCH");
        }

        return pluginRoot;
    }

    private static void EnsureSuccessful(
        DevicePluginDatabaseLifecycleResult result,
        int expectedSchemaVersion,
        string fallbackReasonCode)
    {
        if (!result.Succeeded || result.SchemaVersion != expectedSchemaVersion)
        {
            throw new DevicePluginDatabaseStartupException(
                string.IsNullOrWhiteSpace(result.FailureReasonCode)
                    ? fallbackReasonCode
                    : result.FailureReasonCode);
        }
    }
}

/// <summary>
/// Single in-memory publication point for all v3 readers. GetRequiredSnapshot never touches SQLite.
/// </summary>
public sealed class DevicePluginConfigurationSnapshotCache
    : IDevicePluginConfigurationSnapshotAccessor
{
    private readonly IDevicePluginRuntimeContext _runtimeContext;
    private readonly IDevicePluginConfigurationStoreV1[] _stores;
    private readonly ILogService _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DevicePluginConfigurationSnapshot? _snapshot;
    private long _requiredVersion;

    public DevicePluginConfigurationSnapshotCache(
        IDevicePluginRuntimeContext runtimeContext,
        IEnumerable<IDevicePluginConfigurationStoreV1> stores,
        ILogService logger)
    {
        _runtimeContext = runtimeContext;
        _stores = stores.ToArray();
        _logger = logger;
        foreach (var store in _stores)
        {
            store.ConfigurationChanged += OnConfigurationChanged;
        }
    }

    public bool IsInitialized => Volatile.Read(ref _snapshot) is not null;

    public DevicePluginConfigurationSnapshot GetRequiredSnapshot()
        => Volatile.Read(ref _snapshot)
           ?? throw new DevicePluginDatabaseStartupException(
               "PLUGIN_CONFIGURATION_SNAPSHOT_NOT_INITIALIZED");

    public IReadOnlyList<DevicePluginPlcSnapshot> GetPlcs()
        => GetRequiredSnapshot().Plcs
            .Select(static item => new DevicePluginPlcSnapshot(
                DevicePluginProjectionIds.Plc(item.PlcCode),
                item))
            .ToArray();

    public IReadOnlyList<DevicePluginIoPointSnapshot> GetIoPoints()
        => GetRequiredSnapshot().IoPoints
            .Select(static item => new DevicePluginIoPointSnapshot(
                DevicePluginProjectionIds.Io(item.PlcCode, item.SignalKey),
                DevicePluginProjectionIds.Plc(item.PlcCode),
                item))
            .ToArray();

    public IReadOnlyList<DevicePluginTaskBindingSnapshot> GetTaskBindings()
        => GetRequiredSnapshot().TaskBindings
            .Select(static item => new DevicePluginTaskBindingSnapshot(
                DevicePluginProjectionIds.Binding(item.PlcCode, item.TaskKey),
                DevicePluginProjectionIds.Plc(item.PlcCode),
                item))
            .ToArray();

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_stores.Length != 1)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await _stores[0]
                .GetSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            Validate(snapshot, _runtimeContext.Current);

            var requiredVersion = Volatile.Read(ref _requiredVersion);
            if (snapshot.ConfigurationVersion < requiredVersion)
            {
                throw new DevicePluginDatabaseStartupException(
                    "PLUGIN_CONFIGURATION_VERSION_STALE");
            }

            var current = Volatile.Read(ref _snapshot);
            if (current is null || snapshot.ConfigurationVersion >= current.ConfigurationVersion)
            {
                Volatile.Write(ref _snapshot, snapshot);
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void OnConfigurationChanged(
        object? sender,
        DevicePluginConfigurationChangedEventArgs args)
    {
        UpdateRequiredVersion(args.CurrentVersion);
        Volatile.Write(ref _snapshot, null);
        _ = RefreshAfterChangeAsync(args.CurrentVersion);
    }

    private async Task RefreshAfterChangeAsync(long expectedVersion)
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.Warn(
                $"[PluginDatabase] 配置版本 {expectedVersion} 内存快照刷新失败，" +
                $"已保持 Unavailable（{exception.GetType().Name}）。");
        }
    }

    private void UpdateRequiredVersion(long candidate)
    {
        var observed = Volatile.Read(ref _requiredVersion);
        while (candidate > observed)
        {
            var previous = Interlocked.CompareExchange(
                ref _requiredVersion,
                candidate,
                observed);
            if (previous == observed)
            {
                return;
            }

            observed = previous;
        }
    }

    private static void Validate(
        DevicePluginConfigurationSnapshot snapshot,
        DevicePluginRuntimeIdentity runtime)
    {
        if (!runtime.IsV3
            || !string.Equals(
                snapshot.Identity.NormalizedClientCode,
                DevicePluginIdentity.NormalizeClientCode(runtime.ClientCode),
                StringComparison.Ordinal)
            || !string.Equals(snapshot.Identity.ModuleId, runtime.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.Identity.ProcessType, runtime.ProcessType, StringComparison.OrdinalIgnoreCase)
            || snapshot.ConfigurationVersion < 1
            || HasDuplicates(snapshot.Plcs.Select(static item => item.PlcCode))
            || HasDuplicates(snapshot.IoPoints.Select(static item => $"{item.PlcCode}\u001f{item.SignalKey}"))
            || HasDuplicates(snapshot.TaskBindings.Select(static item => $"{item.PlcCode}\u001f{item.TaskKey}"))
            || HasDuplicates(snapshot.ModuleSettings.Select(static item => item.Key)))
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_CONFIGURATION_SNAPSHOT_INVALID");
        }
    }

    private static bool HasDuplicates(IEnumerable<string> values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !set.Add(value.Trim()))
            {
                return true;
            }
        }

        return false;
    }
}
