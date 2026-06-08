using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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
    private const string BootstrapSecretHeader = "X-IIoT-Bootstrap-Secret";
    private readonly Func<HttpMessageHandler>? _messageHandlerFactory;

    public LauncherEdgeReleaseCloudClient()
    {
    }

    internal LauncherEdgeReleaseCloudClient(Func<HttpMessageHandler> messageHandlerFactory)
    {
        _messageHandlerFactory = messageHandlerFactory;
    }

    public async Task<LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>> BootstrapAsync(
        LauncherCloudApiOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = CreateHttpClient(options);
            var url = BuildUrl(
                options.BaseUrl,
                $"{RequireRelativePath(options.DeviceInstancePath)}?clientCode={Uri.EscapeDataString(options.ClientCode)}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(BootstrapSecretHeader, options.BootstrapSecret);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Failed(
                    await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var dto = await response.Content
                .ReadFromJsonAsync<LauncherDeviceBootstrapDto>(JsonOptions(), cancellationToken)
                .ConfigureAwait(false);
            if (dto is null || dto.Id == Guid.Empty)
            {
                return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Failed("Cloud bootstrap 返回空设备身份。");
            }

            return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Succeeded(
                new LauncherEdgeDeviceSession(
                    dto.Id,
                    dto.DeviceName ?? string.Empty,
                    string.IsNullOrWhiteSpace(dto.ClientCode) ? options.ClientCode : dto.ClientCode!,
                    dto.UploadAccessToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return LauncherEdgeCloudOperationResult<LauncherEdgeDeviceSession>.Failed("Cloud bootstrap 超时。");
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
            using var client = CreateHttpClient(options);
            var path = RequireRelativePath(options.ClientReleaseCatalogTemplate)
                .Replace("{deviceId}", Uri.EscapeDataString(session.DeviceId.ToString()), StringComparison.OrdinalIgnoreCase);
            var query = $"channel={Uri.EscapeDataString(releaseOptions.Channel)}&targetRuntime={Uri.EscapeDataString(releaseOptions.TargetRuntime)}";
            var url = BuildUrl(options.BaseUrl, path.Contains("?", StringComparison.Ordinal) ? $"{path}&{query}" : $"{path}?{query}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.UploadAccessToken);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed(
                    await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var catalog = await response.Content
                .ReadFromJsonAsync<LauncherClientReleaseCatalog>(JsonOptions(), cancellationToken)
                .ConfigureAwait(false);
            return catalog is null
                ? LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed("Cloud catalog 返回空响应。")
                : LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Succeeded(catalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return LauncherEdgeCloudOperationResult<LauncherClientReleaseCatalog>.Failed("Cloud catalog 请求超时。");
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
            using var client = CreateHttpClient(options);
            var url = BuildUrl(options.BaseUrl, RequireRelativePath(options.ClientVersionReportPath));
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.UploadAccessToken);
            request.Content = JsonContent.Create(
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
                    enabledPlugins = enabledPlugins,
                    channel = releaseOptions.Channel,
                    reportedAtUtc = DateTime.UtcNow
                },
                options: JsonOptions());

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? LauncherVersionReportResult.Succeeded()
                : LauncherVersionReportResult.Failed(
                    await ReadFailureAsync(response, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return LauncherVersionReportResult.Failed("版本上报超时。");
        }
        catch (Exception ex)
        {
            return LauncherVersionReportResult.Failed($"版本上报失败: {ex.Message}");
        }
    }

    private HttpClient CreateHttpClient(LauncherCloudApiOptions options)
    {
        var client = _messageHandlerFactory is null
            ? new HttpClient()
            : new HttpClient(_messageHandlerFactory(), disposeHandler: true);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 120));
        return client;
    }

    internal static Uri BuildUrl(string baseUrl, string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            return absolute;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"CloudApi:BaseUrl 无效: {baseUrl}");
        }

        return new Uri(baseUri, relativeOrAbsoluteUrl.TrimStart('/'));
    }

    private static string RequireRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Cloud API path 为空。");
        }

        return path.Trim();
    }

    private static async Task<string> ReadFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? $"Cloud 请求失败: HTTP {(int)response.StatusCode}";
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var first = errors.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String)
                {
                    return first.GetString() ?? $"Cloud 请求失败: HTTP {(int)response.StatusCode}";
                }
            }
        }
        catch
        {
        }

        return $"Cloud 请求失败: HTTP {(int)response.StatusCode}";
    }

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

    private sealed class LauncherDeviceBootstrapDto
    {
        public Guid Id { get; set; }
        public string? DeviceName { get; set; }
        public string? ClientCode { get; set; }
        public string? UploadAccessToken { get; set; }
    }
}
