using System.Globalization;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Identity;

namespace IIoT.Edge.Application.Features.Production.Monitor;

/// <summary>
/// 监控设备身份匹配的纯函数集合，不访问运行时、数据库或外部服务。
/// </summary>
internal static class MonitorDeviceIdentityHelper
{
    public static bool HasContextForRuntimeStatus(
        IReadOnlyCollection<ProductionContext> contexts,
        PlcConnectionRuntimeSnapshot runtimeStatus)
        => contexts.Any(context => MatchesDevice(context, runtimeStatus));

    public static string RuntimeStatusKey(PlcConnectionRuntimeSnapshot runtimeStatus)
        => runtimeStatus.NetworkDeviceId > 0
            ? $"id:{runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)}"
            : $"name:{runtimeStatus.DeviceName}";

    public static bool HasMonitorSourceForConfiguredDevice(
        IReadOnlyCollection<ProductionContext> contexts,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> runtimeStatuses,
        NetworkDeviceEntity device)
        => contexts.Any(context => MatchesDevice(context, device))
            || runtimeStatuses.Any(runtimeStatus => MatchesDevice(runtimeStatus, device));

    public static string ConfiguredDeviceKey(NetworkDeviceEntity device)
        => device.Id > 0
            ? $"id:{device.Id.ToString(CultureInfo.InvariantCulture)}"
            : $"name:{device.DeviceName}";

    public static int ResolveNetworkDeviceId(
        int contextNetworkDeviceId,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        NetworkDeviceEntity? configuredDevice)
    {
        if (contextNetworkDeviceId > 0)
        {
            return contextNetworkDeviceId;
        }

        if (runtimeStatus?.NetworkDeviceId > 0)
        {
            return runtimeStatus.NetworkDeviceId;
        }

        return configuredDevice?.Id ?? 0;
    }

    public static string ResolveDeviceName(
        string? contextDeviceName,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        NetworkDeviceEntity? configuredDevice)
    {
        if (!string.IsNullOrWhiteSpace(contextDeviceName))
        {
            return contextDeviceName;
        }

        if (!string.IsNullOrWhiteSpace(runtimeStatus?.DeviceName))
        {
            return runtimeStatus.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(configuredDevice?.DeviceName))
        {
            return configuredDevice.DeviceName;
        }

        return runtimeStatus?.NetworkDeviceId > 0
            ? runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)
            : "--";
    }

    public static string FormatEndpoint(NetworkDeviceEntity? device)
    {
        if (device is null)
        {
            return "--";
        }

        var endpoint = $"{device.IpAddress}:{device.Port1.ToString(CultureInfo.InvariantCulture)}";
        return device.Port2.HasValue
            ? $"{endpoint}/{device.Port2.Value.ToString(CultureInfo.InvariantCulture)}"
            : endpoint;
    }

    public static DateTimeOffset? ResolveLatestRuntimeTimestamp(PlcConnectionRuntimeSnapshot runtimeStatus)
    {
        var candidates = new[]
            {
                runtimeStatus.LastConnectedAtUtc,
                runtimeStatus.LastFailureAtUtc
            }
            .Where(static value => value.HasValue && value.Value.Year > 1900)
            .Select(static value => value!.Value)
            .OrderByDescending(static value => value)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static bool MatchesDevice(IDeviceIdentifiable source, IDeviceIdentifiable target)
        => (target.NetworkDeviceId > 0
                && source.NetworkDeviceId == target.NetworkDeviceId)
            || !string.IsNullOrWhiteSpace(source.DeviceName)
                && string.Equals(source.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase);
}
