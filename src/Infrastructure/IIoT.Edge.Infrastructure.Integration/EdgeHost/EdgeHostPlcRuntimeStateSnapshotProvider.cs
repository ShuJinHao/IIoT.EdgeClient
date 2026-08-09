using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.Integration.EdgeHost;

public sealed class EdgeHostPlcRuntimeStateSnapshotProvider(
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IPlcConnectionManager plcConnectionManager,
    IDeviceService deviceService,
    IPlcConfigurationVersionStore configurationVersionStore)
    : IEdgeHostPlcRuntimeStateSnapshotProvider,
      IAuthoritativePlcSnapshotProvider,
      IPlcConfigurationSnapshotInvalidator
{
    private const string Connected = "Connected";
    private const string Disconnected = "Disconnected";
    private const string Faulted = "Faulted";
    private const string Unknown = "Unknown";

    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly object _snapshotSync = new();
    private ConfigurationSnapshot? _configurationSnapshot;
    private long _requestedVersion;

    public async Task<IReadOnlyList<EdgeHostPlcRuntimeStateReportItem>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var authoritative = await GetAuthoritativeSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!authoritative.IsAuthoritative)
        {
            throw new InvalidOperationException(
                authoritative.UnavailableReason ?? "PLC authoritative snapshot is unavailable.");
        }

        return authoritative.Items
            .Select(static item => new EdgeHostPlcRuntimeStateReportItem(
                item.PlcCode,
                item.PlcName,
                item.ConnectionState == PlcConnectionState.Connected,
                ResolveRuntimeStatus(item.ConnectionState, item.LastError),
                item.LastRealCommunicationAtUtc?.UtcDateTime,
                Protocol: item.Protocol,
                Address: FormatAddress(item.IpAddress, item.Port),
                LastError: item.LastError))
            .ToArray();
    }

    Task<AuthoritativePlcSnapshot> IAuthoritativePlcSnapshotProvider.GetCurrentAsync(
        CancellationToken cancellationToken)
        => GetAuthoritativeSnapshotAsync(cancellationToken);

    public void Invalidate()
    {
        var clientCode = deviceService.CurrentDevice?.ClientCode?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            throw new InvalidOperationException(
                "Cannot advance PLC configuration version before ClientCode is identified.");
        }

        Interlocked.Exchange(
            ref _requestedVersion,
            configurationVersionStore.Advance(clientCode));
        lock (_snapshotSync)
        {
            _configurationSnapshot = null;
        }
    }

    public async Task WarmAsync(CancellationToken cancellationToken = default)
    {
        var clientCode = deviceService.CurrentDevice?.ClientCode?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            throw new InvalidOperationException("Cannot warm PLC snapshot before ClientCode is identified.");
        }

        _ = await GetConfigurationSnapshotAsync(clientCode, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<AuthoritativePlcSnapshot> GetAuthoritativeSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var clientCode = deviceService.CurrentDevice?.ClientCode?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            return Unavailable("device_unidentified");
        }

        ConfigurationSnapshot configuration;
        try
        {
            configuration = await GetConfigurationSnapshotAsync(clientCode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Unavailable($"configuration_load_failed:{ex.GetType().Name}", clientCode);
        }

        var runtimeSnapshots = plcConnectionManager.GetRuntimeStatuses();
        var reportItems = BuildSnapshotItems(configuration.Devices, runtimeSnapshots);
        return new AuthoritativePlcSnapshot(
            clientCode,
            AuthoritativePlcSnapshotStatus.Authoritative,
            configuration.Version,
            DateTimeOffset.UtcNow,
            ClearProjection: reportItems.Count == 0,
            reportItems);
    }

    private AuthoritativePlcSnapshot Unavailable(string reason, string? clientCode = null)
        => new(
            clientCode ?? deviceService.CurrentDevice?.ClientCode?.Trim() ?? string.Empty,
            AuthoritativePlcSnapshotStatus.Unavailable,
            Math.Max(0, Volatile.Read(ref _requestedVersion)),
            DateTimeOffset.UtcNow,
            ClearProjection: false,
            Array.Empty<AuthoritativePlcSnapshotItem>(),
            reason);

    private async Task<ConfigurationSnapshot> GetConfigurationSnapshotAsync(
        string clientCode,
        CancellationToken cancellationToken)
    {
        lock (_snapshotSync)
        {
            if (_configurationSnapshot is not null)
            {
                return _configurationSnapshot;
            }
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                lock (_snapshotSync)
                {
                    if (_configurationSnapshot is not null)
                    {
                        return _configurationSnapshot;
                    }
                }

                var requestedVersion = Volatile.Read(ref _requestedVersion);
                if (requestedVersion <= 0)
                {
                    requestedVersion = configurationVersionStore.ReadOrCreate(clientCode);
                    Interlocked.CompareExchange(ref _requestedVersion, requestedVersion, 0);
                    requestedVersion = Volatile.Read(ref _requestedVersion);
                }
                var configuredPlcs = await networkDevices
                    .GetListAsync(static device => device.DeviceType == DeviceType.PLC, cancellationToken)
                    .ConfigureAwait(false);
                var immutableDevices = configuredPlcs
                    .Select(static device => ConfiguredPlc.From(device))
                    .OrderBy(static device => device.PlcName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (requestedVersion != Volatile.Read(ref _requestedVersion))
                {
                    continue;
                }

                var snapshot = new ConfigurationSnapshot(requestedVersion, immutableDevices);
                lock (_snapshotSync)
                {
                    if (requestedVersion == Volatile.Read(ref _requestedVersion))
                    {
                        return _configurationSnapshot = snapshot;
                    }
                }
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    internal static IReadOnlyList<AuthoritativePlcSnapshotItem> BuildSnapshotItems(
        IReadOnlyCollection<ConfiguredPlc> configuredPlcs,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> runtimeSnapshots)
    {
        var snapshotsById = runtimeSnapshots
            .Where(static snapshot => snapshot.NetworkDeviceId > 0)
            .GroupBy(static snapshot => snapshot.NetworkDeviceId)
            .Where(static group => group.Take(2).Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First());
        var snapshotsByPlcCode = runtimeSnapshots
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.PlcCode))
            .GroupBy(static snapshot => snapshot.PlcCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Take(2).Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        return configuredPlcs
            .Where(static device => !string.IsNullOrWhiteSpace(device.PlcName))
            .Select(device =>
            {
                PlcConnectionRuntimeSnapshot? runtime = null;
                if (!string.IsNullOrWhiteSpace(device.PlcCode))
                {
                    snapshotsByPlcCode.TryGetValue(device.PlcCode, out runtime);
                    if (runtime is null
                        && snapshotsById.TryGetValue(device.Id, out var legacy)
                        && string.IsNullOrWhiteSpace(legacy.PlcCode))
                    {
                        runtime = legacy;
                    }
                }
                else
                {
                    snapshotsById.TryGetValue(device.Id, out runtime);
                }

                var state = runtime?.ConnectionState ?? PlcConnectionState.Unknown;
                if (runtime?.IsConnected == true)
                {
                    state = PlcConnectionState.Connected;
                }
                else if (!string.IsNullOrWhiteSpace(runtime?.LastError))
                {
                    state = PlcConnectionState.Faulted;
                }

                return new AuthoritativePlcSnapshotItem(
                    device.PlcCode,
                    FirstNonEmpty(device.PlcName, runtime?.DeviceName),
                    device.IpAddress,
                    device.Port,
                    device.Protocol,
                    device.IsEnabled,
                    state,
                    ResolveObservedAtUtc(runtime),
                    Normalize(runtime?.LastError));
            })
            .ToArray();
    }

    private static string ResolveRuntimeStatus(PlcConnectionState state, string? lastError)
    {
        if (state == PlcConnectionState.Connected)
        {
            return Connected;
        }

        if (state == PlcConnectionState.Faulted || !string.IsNullOrWhiteSpace(lastError))
        {
            return Faulted;
        }

        return state is PlcConnectionState.Unknown or PlcConnectionState.Connecting
            ? Unknown
            : Disconnected;
    }

    private static DateTimeOffset? ResolveObservedAtUtc(PlcConnectionRuntimeSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new[]
            {
                snapshot.LastReadAtUtc,
                snapshot.LastConnectedAtUtc,
                snapshot.LastFailureAtUtc,
                snapshot.LastAttemptAtUtc,
                snapshot.StateChangedAtUtc
            }
            .Where(static value => value.HasValue && value.Value.Year > 1900)
            .Select(static value => value!.Value)
            .OrderByDescending(static value => value)
            .FirstOrDefault();
    }

    private static string? FormatAddress(string? ipAddress, int? port)
        => string.IsNullOrWhiteSpace(ipAddress)
            ? null
            : port.HasValue
                ? $"{ipAddress.Trim()}:{port.Value}"
                : ipAddress.Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record ConfiguredPlc(
        int Id,
        string PlcCode,
        string PlcName,
        string? IpAddress,
        int? Port,
        string? Protocol,
        bool IsEnabled)
    {
        public static ConfiguredPlc From(NetworkDeviceEntity entity)
            => new(
                entity.Id,
                entity.PlcCode?.Trim() ?? string.Empty,
                entity.DeviceName?.Trim() ?? string.Empty,
                Normalize(entity.IpAddress),
                entity.Port1,
                FirstNonEmpty(entity.ProtocolFrame, entity.DeviceModel),
                entity.IsEnabled);
    }

    private sealed record ConfigurationSnapshot(
        long Version,
        IReadOnlyList<ConfiguredPlc> Devices);
}
