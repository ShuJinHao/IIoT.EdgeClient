using System.Globalization;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Application.Common.Plugins;
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
        => !string.IsNullOrWhiteSpace(runtimeStatus.PlcCode)
            ? $"plc:{runtimeStatus.PlcCode}"
            : runtimeStatus.NetworkDeviceId > 0
            ? $"id:{runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)}"
            : "unresolved";

    public static bool HasMonitorSourceForConfiguredDevice(
        IReadOnlyCollection<ProductionContext> contexts,
        IReadOnlyCollection<PlcConnectionRuntimeSnapshot> runtimeStatuses,
        DevicePluginPlcSnapshot device)
        => contexts.Any(context => MatchesDevice(context, device))
            || runtimeStatuses.Any(runtimeStatus => MatchesDevice(runtimeStatus, device));

    public static string ConfiguredDeviceKey(DevicePluginPlcSnapshot device)
        => !string.IsNullOrWhiteSpace(device.PlcCode)
            ? $"plc:{device.PlcCode}"
            : device.Id > 0
            ? $"id:{device.Id.ToString(CultureInfo.InvariantCulture)}"
            : "unresolved";

    public static int ResolveNetworkDeviceId(
        int contextNetworkDeviceId,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        DevicePluginPlcSnapshot? configuredDevice)
    {
        if (configuredDevice?.Id > 0)
        {
            return configuredDevice.Id;
        }

        if (runtimeStatus?.NetworkDeviceId > 0)
        {
            return runtimeStatus.NetworkDeviceId;
        }

        return contextNetworkDeviceId > 0 ? contextNetworkDeviceId : 0;
    }

    public static string ResolveDeviceName(
        string? contextDeviceName,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        DevicePluginPlcSnapshot? configuredDevice)
    {
        if (!string.IsNullOrWhiteSpace(configuredDevice?.DeviceName))
        {
            return configuredDevice.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(runtimeStatus?.DeviceName))
        {
            return runtimeStatus.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(contextDeviceName))
        {
            return contextDeviceName;
        }

        return runtimeStatus?.NetworkDeviceId > 0
            ? runtimeStatus.NetworkDeviceId.ToString(CultureInfo.InvariantCulture)
            : "--";
    }

    public static string ResolvePlcCode(
        string? contextPlcCode,
        PlcConnectionRuntimeSnapshot? runtimeStatus,
        DevicePluginPlcSnapshot? configuredDevice)
        => new[]
            {
                contextPlcCode,
                runtimeStatus?.PlcCode,
                configuredDevice?.PlcCode
            }
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?.Trim()
            ?? string.Empty;

    public static string FormatEndpoint(DevicePluginPlcSnapshot? device)
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
    {
        if (source is IPlcIdentifiable sourcePlc
            && target is IPlcIdentifiable targetPlc
            && !string.IsNullOrWhiteSpace(sourcePlc.PlcCode)
            && !string.IsNullOrWhiteSpace(targetPlc.PlcCode))
        {
            return string.Equals(
                sourcePlc.PlcCode,
                targetPlc.PlcCode,
                StringComparison.OrdinalIgnoreCase);
        }

        return target.NetworkDeviceId > 0
               && source.NetworkDeviceId == target.NetworkDeviceId;
    }
}
