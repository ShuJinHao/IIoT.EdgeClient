using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.Integration.EdgeHost;

public sealed class EdgeHostPlcRuntimeStateSnapshotProvider(
    IRepository<NetworkDeviceEntity> networkDevices,
    IPlcConnectionManager plcConnectionManager) : IEdgeHostPlcRuntimeStateSnapshotProvider
{
    private const string Connected = "Connected";
    private const string Disconnected = "Disconnected";
    private const string Faulted = "Faulted";
    private const string Unknown = "Unknown";

    public async Task<IReadOnlyList<EdgeHostPlcRuntimeStateReportItem>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        var configuredPlcs = await networkDevices
            .GetListAsync(static device => device.DeviceType == DeviceType.PLC, cancellationToken)
            .ConfigureAwait(false);
        var runtimeSnapshots = plcConnectionManager.GetRuntimeStatuses();

        return BuildReportItems(configuredPlcs, runtimeSnapshots);
    }

    internal static IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> BuildReportItems(
        IReadOnlyCollection<NetworkDeviceEntity> configuredPlcs,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> runtimeSnapshots)
    {
        var normalizedConfiguredPlcs = configuredPlcs
            .Where(static device => !string.IsNullOrWhiteSpace(device.DeviceName))
            .OrderBy(static device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var snapshotsById = runtimeSnapshots
            .Where(static snapshot => snapshot.NetworkDeviceId > 0)
            .GroupBy(static snapshot => snapshot.NetworkDeviceId)
            .ToDictionary(static group => group.Key, static group => group.First());
        var snapshotsByName = runtimeSnapshots
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.DeviceName))
            .GroupBy(static snapshot => snapshot.DeviceName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (normalizedConfiguredPlcs.Length > 0)
        {
            return normalizedConfiguredPlcs
                .Select(device =>
                {
                    snapshotsById.TryGetValue(device.Id, out var snapshot);
                    if (snapshot is null)
                    {
                        snapshotsByName.TryGetValue(device.DeviceName.Trim(), out snapshot);
                    }

                    return CreateReportItem(device, snapshot);
                })
                .ToArray();
        }

        return runtimeSnapshots
            .Where(static snapshot => !string.IsNullOrWhiteSpace(snapshot.DeviceName))
            .OrderBy(static snapshot => snapshot.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(static snapshot => CreateReportItem(null, snapshot))
            .ToArray();
    }

    private static EdgeHostPlcRuntimeStateReportItem CreateReportItem(
        NetworkDeviceEntity? configuredDevice,
        PlcConnectionRuntimeSnapshot? runtimeSnapshot)
    {
        var plcName = FirstNonEmpty(configuredDevice?.DeviceName, runtimeSnapshot?.DeviceName);
        if (runtimeSnapshot is null)
        {
            return new EdgeHostPlcRuntimeStateReportItem(
                PlcCode: plcName,
                ReportedPlcName: plcName,
                IsConnected: false,
                RuntimeStatus: Unknown,
                Protocol: FirstNonEmpty(configuredDevice?.ProtocolFrame, configuredDevice?.DeviceModel),
                Address: FormatAddress(configuredDevice));
        }

        var runtimeStatus = ResolveRuntimeStatus(runtimeSnapshot);
        return new EdgeHostPlcRuntimeStateReportItem(
            PlcCode: plcName,
            ReportedPlcName: plcName,
            IsConnected: string.Equals(runtimeStatus, Connected, StringComparison.Ordinal),
            RuntimeStatus: runtimeStatus,
            ObservedAtUtc: ResolveObservedAtUtc(runtimeSnapshot),
            Protocol: FirstNonEmpty(configuredDevice?.ProtocolFrame, configuredDevice?.DeviceModel),
            Address: FormatAddress(configuredDevice),
            LastError: Normalize(runtimeSnapshot.LastError));
    }

    private static string ResolveRuntimeStatus(PlcConnectionRuntimeSnapshot snapshot)
    {
        if (snapshot.ConnectionState == PlcConnectionState.Faulted)
        {
            return Faulted;
        }

        if (snapshot.IsConnected)
        {
            return Connected;
        }

        return snapshot.ConnectionState switch
        {
            PlcConnectionState.Unknown => Unknown,
            _ => Disconnected
        };
    }

    private static DateTime? ResolveObservedAtUtc(PlcConnectionRuntimeSnapshot snapshot)
    {
        var latest = new[]
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

        return latest == default ? null : latest.UtcDateTime;
    }

    private static string? FormatAddress(NetworkDeviceEntity? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return null;
        }

        var endpoint = $"{device.IpAddress.Trim()}:{device.Port1}";
        return device.Port2.HasValue ? $"{endpoint}/{device.Port2.Value}" : endpoint;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
