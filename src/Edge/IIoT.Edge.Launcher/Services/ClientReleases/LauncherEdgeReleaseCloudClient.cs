using System.Net.Http.Headers;
using IIoT.Edge.Infrastructure.CloudClient;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherEdgeReleaseCloudClient
{
    Task<LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>> BootstrapAsync(
        LauncherCloudApiOptions options,
        CancellationToken cancellationToken = default);

    Task<LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>> GetCatalogAsync(
        LauncherCloudApiOptions options,
        LauncherEdgeDeviceSession session,
        LauncherClientReleaseOptions releaseOptions,
        CancellationToken cancellationToken = default);

    Task<LauncherVersionReportResult> ReportVersionAsync(
        LauncherCloudApiOptions options,
        LauncherEdgeDeviceSession session,
        LauncherClientReleaseOptions releaseOptions,
        string machineProfile,
        string hostVersion,
        string hostApiVersion,
        IReadOnlyList<LauncherInstalledPlugin> installedPlugins,
        IReadOnlyList<string> enabledPlugins,
        CancellationToken cancellationToken = default);
}

public sealed class LauncherEdgeReleaseCloudClient : ILauncherEdgeReleaseCloudClient
{
    private readonly ICloudClientHttpTransport _transport;
    private readonly IEdgeCloudDeviceBootstrapClient _bootstrapClient;

    public LauncherEdgeReleaseCloudClient(
        ICloudClientHttpTransport transport,
        IEdgeCloudDeviceBootstrapClient bootstrapClient)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _bootstrapClient = bootstrapClient ?? throw new ArgumentNullException(nameof(bootstrapClient));
    }

    internal LauncherEdgeReleaseCloudClient(Func<HttpMessageHandler> messageHandlerFactory)
        : this(
            new CloudClientHttpTransport(
                () => new HttpClient(messageHandlerFactory())
                {
                    Timeout = Timeout.InfiniteTimeSpan
                }),
            new EdgeCloudDeviceBootstrapClient(
                new CloudClientHttpTransport(
                    () => new HttpClient(messageHandlerFactory())
                    {
                        Timeout = Timeout.InfiniteTimeSpan
                    })))
    {
    }

    public async Task<LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>> BootstrapAsync(
        LauncherCloudApiOptions options,
        CancellationToken cancellationToken = default)
    {
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
                return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Failed(
                    result.ErrorMessage ?? "Cloud bootstrap 返回空设备身份。");
            }

            return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Succeeded(
                new LauncherEdgeDeviceSession(
                    result.Session.DeviceId,
                    result.Session.DeviceName,
                    string.IsNullOrWhiteSpace(result.Session.ClientCode) ? options.ClientCode : result.Session.ClientCode,
                    result.Session.UploadAccessToken));
        }
        catch (Exception ex)
        {
            return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Failed(
                $"Cloud bootstrap 失败: {ex.Message}");
        }
    }

    public async Task<LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>> GetCatalogAsync(
        LauncherCloudApiOptions options,
        LauncherEdgeDeviceSession session,
        LauncherClientReleaseOptions releaseOptions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.UploadAccessToken))
        {
            return LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed("设备 token 为空，无法拉取 catalog。");
        }

        try
        {
            var path = RequireRelativePath(options.ClientReleaseCatalogTemplate)
                .Replace("{deviceId}", Uri.EscapeDataString(session.DeviceId.ToString()), StringComparison.OrdinalIgnoreCase);
            var query = $"channel={Uri.EscapeDataString(releaseOptions.Channel)}&targetRuntime={Uri.EscapeDataString(releaseOptions.TargetRuntime)}";
            var url = BuildUrl(options.BaseUrl, path.Contains("?", StringComparison.Ordinal) ? $"{path}&{query}" : $"{path}?{query}");
            var catalog = await _transport
                .GetJsonAsync<LauncherClientReleaseCatalog>(
                    url,
                    ResolveTimeout(options),
                    headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", session.UploadAccessToken),
                    cancellationToken)
                .ConfigureAwait(false);
            return !catalog.Success || catalog.Value is null
                ? LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed(
                    catalog.ErrorMessage ?? "Cloud catalog 返回空响应。")
                : LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Succeeded(catalog.Value);
        }
        catch (Exception ex)
        {
            return LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed(
                $"Cloud catalog 请求失败: {ex.Message}");
        }
    }

    public async Task<LauncherVersionReportResult> ReportVersionAsync(
        LauncherCloudApiOptions options,
        LauncherEdgeDeviceSession session,
        LauncherClientReleaseOptions releaseOptions,
        string machineProfile,
        string hostVersion,
        string hostApiVersion,
        IReadOnlyList<LauncherInstalledPlugin> installedPlugins,
        IReadOnlyList<string> enabledPlugins,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.UploadAccessToken))
        {
            return LauncherVersionReportResult.Failed("设备 token 为空，无法上报版本。");
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
                        machineProfile,
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
                        reportedAtUtc = DateTime.UtcNow
                    },
                    ResolveTimeout(options),
                    headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", session.UploadAccessToken),
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Success
                ? LauncherVersionReportResult.Succeeded()
                : LauncherVersionReportResult.Failed(
                    response.ErrorMessage ?? $"Cloud 请求失败: HTTP {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return LauncherVersionReportResult.Failed($"版本上报失败: {ex.Message}");
        }
    }

    internal static Uri BuildUrl(string baseUrl, string relativeOrAbsoluteUrl)
        => CloudClientHttpUrl.Build(baseUrl, relativeOrAbsoluteUrl);

    private static TimeSpan ResolveTimeout(LauncherCloudApiOptions options)
        => TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));

    private static string RequireRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Cloud API path 为空。");
        }

        return path.Trim();
    }
}
