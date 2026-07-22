using System.Net.Http.Headers;
using System.Net;
using System.Net.NetworkInformation;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Infrastructure.CloudClient;
using static IIoT.Edge.Infrastructure.Update.Cloud.EdgeUpdateCloudUrl;

namespace IIoT.Edge.Infrastructure.Update.Cloud;

public sealed class HttpEdgeUpdateDeviceSessionClient : IEdgeUpdateDeviceSessionClient
{
    private readonly IEdgeCloudDeviceBootstrapClient _bootstrapClient;

    public HttpEdgeUpdateDeviceSessionClient(IEdgeCloudDeviceBootstrapClient bootstrapClient)
    {
        _bootstrapClient = bootstrapClient ?? throw new ArgumentNullException(nameof(bootstrapClient));
    }

    public async Task<EdgeUpdateOperationResult<EdgeUpdateDeviceSession>> BootstrapAsync(
        EdgeUpdateCloudApiOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var url = BuildUrl(
                options.BaseUrl,
                $"{RequireRelativePath(options.DeviceInstancePath)}?clientCode={Uri.EscapeDataString(options.ClientCode)}");
            var result = await _bootstrapClient
                .BootstrapAsync(
                    new EdgeCloudDeviceBootstrapRequest(
                        options.ClientCode,
                        options.BootstrapSecret,
                        url,
                        ResolveTimeout(options)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Kind != EdgeCloudDeviceBootstrapResultKind.Success
                || result.Session is null
                || result.Session.DeviceId == Guid.Empty)
            {
                return EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Failed(
                    result.ErrorMessage ?? "Cloud bootstrap 返回空设备身份。");
            }

            return EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Succeeded(
                new EdgeUpdateDeviceSession(
                    result.Session.DeviceId,
                    result.Session.DeviceName,
                    string.IsNullOrWhiteSpace(result.Session.ClientCode) ? options.ClientCode : result.Session.ClientCode,
                    result.Session.UploadAccessToken));
        }
        catch (Exception ex)
        {
            return EdgeUpdateOperationResult<EdgeUpdateDeviceSession>.Failed(
                $"Cloud bootstrap 失败: {ex.Message}");
        }
    }
}

public sealed class HttpEdgeUpdateCatalogClient : IEdgeUpdateCatalogClient
{
    private readonly ICloudClientHttpTransport _transport;

    public HttpEdgeUpdateCatalogClient(ICloudClientHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<EdgeUpdateOperationResult<EdgeReleaseCatalog>> GetCatalogAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeReleaseOptions releaseOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(releaseOptions);

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed("设备 token 为空，无法拉取 catalog。");
        }

        try
        {
            var path = RequireRelativePath(options.ClientReleaseCatalogTemplate)
                .Replace("{deviceId}", Uri.EscapeDataString(session.DeviceId.ToString()), StringComparison.OrdinalIgnoreCase);
            var query = $"channel={Uri.EscapeDataString(releaseOptions.Channel)}&targetRuntime={Uri.EscapeDataString(releaseOptions.TargetRuntime)}";
            var url = BuildUrl(options.BaseUrl, path.Contains("?", StringComparison.Ordinal) ? $"{path}&{query}" : $"{path}?{query}");
            var catalog = await _transport
                .GetJsonAsync<EdgeReleaseCatalog>(
                    url,
                    ResolveTimeout(options),
                    headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken),
                    cancellationToken)
                .ConfigureAwait(false);
            return !catalog.Success || catalog.Value is null
                ? EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(
                    catalog.ErrorMessage ?? "Cloud catalog 返回空响应。")
                : EdgeUpdateOperationResult<EdgeReleaseCatalog>.Succeeded(catalog.Value);
        }
        catch (Exception ex)
        {
            return EdgeUpdateOperationResult<EdgeReleaseCatalog>.Failed(
                $"Cloud catalog 请求失败: {ex.Message}");
        }
    }
}

public sealed class HttpEdgeVersionReporter : IEdgeVersionReporter
{
    private readonly ICloudClientHttpTransport _transport;

    public HttpEdgeVersionReporter(ICloudClientHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<EdgeVersionReportResult> ReportVersionAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeReleaseOptions releaseOptions,
        EdgeUpdateTarget target,
        string hostVersion,
        string hostApiVersion,
        IReadOnlyList<EdgeInstalledPlugin> installedPlugins,
        IReadOnlyList<string> enabledPlugins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(releaseOptions);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(installedPlugins);
        ArgumentNullException.ThrowIfNull(enabledPlugins);

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return EdgeVersionReportResult.Failed("设备 token 为空，无法上报版本。");
        }

        try
        {
            var url = BuildUrl(options.BaseUrl, RequireRelativePath(options.ClientVersionReportPath));
            var response = await _transport
                .PostJsonAsync(
                    url,
                    new
                    {
                        deviceId = session.DeviceId,
                        clientCode = session.ClientCode,
                        machineProfile = target.MachineProfile,
                        hostVersion,
                        hostApiVersion,
                        installedPlugins = installedPlugins.Select(plugin => new
                        {
                            moduleId = plugin.ModuleId,
                            displayName = plugin.DisplayName,
                            version = plugin.Version,
                            hostApiVersion = plugin.HostApiVersion
                        }).ToArray(),
                        enabledPlugins,
                        channel = releaseOptions.Channel,
                        reportedAtUtc = DateTime.UtcNow,
                        localIpAddresses = GetLocalIpAddresses()
                    },
                    ResolveTimeout(options),
                    headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken),
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Success
                ? EdgeVersionReportResult.Succeeded()
                : EdgeVersionReportResult.Failed(
                    response.ErrorMessage ?? $"Cloud 请求失败: HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return EdgeVersionReportResult.Failed($"版本上报失败: {ex.Message}");
        }
    }

    private static IReadOnlyList<string> GetLocalIpAddresses()
        => EdgeUpdateLocalIpAddressProvider.GetLocalIpAddresses();
}

public sealed class HttpEdgeRuntimeHeartbeatReporter : IEdgeRuntimeHeartbeatReporter
{
    private readonly ICloudClientHttpTransport _transport;

    public HttpEdgeRuntimeHeartbeatReporter(ICloudClientHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<EdgeRuntimeHeartbeatReportResult> ReportAsync(
        EdgeUpdateCloudApiOptions options,
        EdgeUpdateDeviceSession session,
        EdgeRuntimeHeartbeatReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(report);

        if (string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return EdgeRuntimeHeartbeatReportResult.Failed("设备 token 为空，无法上报运行心跳。");
        }

        try
        {
            var url = BuildUrl(options.BaseUrl, RequireRelativePath(options.RuntimeHeartbeatPath));
            var response = await _transport
                .PostJsonAsync(
                    url,
                    new
                    {
                        deviceId = session.DeviceId,
                        clientCode = session.ClientCode,
                        runtimeInstanceId = report.RuntimeInstanceId,
                        machineProfile = report.MachineProfile,
                        hostVersion = report.HostVersion,
                        hostApiVersion = report.HostApiVersion,
                        status = report.Status.ToString(),
                        startedAtUtc = report.StartedAtUtc,
                        reportedAtUtc = report.ReportedAtUtc,
                        localIpAddresses = EdgeUpdateLocalIpAddressProvider.GetLocalIpAddresses()
                    },
                    ResolveTimeout(options),
                    headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken),
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Success
                ? EdgeRuntimeHeartbeatReportResult.Succeeded()
                : EdgeRuntimeHeartbeatReportResult.Failed(
                    response.ErrorMessage ?? $"Cloud 请求失败: HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return EdgeRuntimeHeartbeatReportResult.Failed($"运行心跳上报失败: {ex.Message}");
        }
    }
}

internal static class EdgeUpdateLocalIpAddressProvider
{
    public static IReadOnlyList<string> GetLocalIpAddresses()
    {
        try
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up
                    && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(address => address.Address)
                .Where(address => !IPAddress.IsLoopback(address)
                    && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}

public static class EdgeUpdateCloudUrl
{
    public static Uri BuildUrl(string baseUrl, string relativeOrAbsoluteUrl)
        => CloudClientHttpUrl.Build(baseUrl, relativeOrAbsoluteUrl);

    internal static TimeSpan ResolveTimeout(EdgeUpdateCloudApiOptions options)
        => TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));

    internal static string RequireRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Cloud API path 为空。");
        }

        return path.Trim();
    }
}
