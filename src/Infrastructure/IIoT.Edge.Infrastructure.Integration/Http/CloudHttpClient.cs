using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Integration.Config;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Infrastructure.Integration.Http;

/// <summary>
/// Cloud HTTP client.
/// Keeps the bool/null contract but emits diagnostics for failures.
/// </summary>
public class CloudHttpClient : ICloudHttpClient
{
    private static readonly HashSet<string> BlockedIdentityKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "macAddress",
        "mac_address",
        "clientCode",
        "client_code"
    };

    private static readonly HashSet<HttpMethod> AnonymousMethods = [HttpMethod.Get, HttpMethod.Post];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudAccessTokenProvider _tokenProvider;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly ILogService _logger;

    public CloudHttpClient(
        IHttpClientFactory httpClientFactory,
        ICloudAccessTokenProvider tokenProvider,
        ICloudApiEndpointProvider endpointProvider,
        ILogService logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _endpointProvider = endpointProvider;
        _logger = logger;
    }

    public async Task<bool> PostAsync(string url, object payload)
    {
        var requestUrl = url;

        try
        {
            var client = _httpClientFactory.CreateClient("CloudApi");
            requestUrl = _endpointProvider.BuildUrl(url);
            var sanitizedPayload = SanitizePayload(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(sanitizedPayload)
            };

            if (!PrepareAuthorization(request))
            {
                return false;
            }

            var response = await client.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.Warn($"[CloudHttp] POST failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] POST exception: {requestUrl}, {ex.Message}");
            return false;
        }
    }

    public async Task<string?> PostWithResponseAsync(string url, object payload)
    {
        var requestUrl = url;

        try
        {
            var client = _httpClientFactory.CreateClient("CloudApi");
            requestUrl = _endpointProvider.BuildUrl(url);
            var sanitizedPayload = SanitizePayload(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = JsonContent.Create(sanitizedPayload)
            };

            if (!PrepareAuthorization(request))
            {
                return null;
            }

            var response = await client.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            _logger.Warn($"[CloudHttp] POST-with-response failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] POST-with-response exception: {requestUrl}, {ex.Message}");
            return null;
        }
    }

    public async Task<string?> GetAsync(string url)
    {
        var requestUrl = url;

        try
        {
            var client = _httpClientFactory.CreateClient("CloudApi");
            requestUrl = _endpointProvider.BuildUrl(url);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            if (!PrepareAuthorization(request))
            {
                return null;
            }

            var response = await client.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            _logger.Warn($"[CloudHttp] GET failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] GET exception: {requestUrl}, {ex.Message}");
            return null;
        }
    }

    private bool PrepareAuthorization(HttpRequestMessage request)
    {
        if (IsAnonymousRequest(request))
        {
            return true;
        }

        var token = _tokenProvider.AccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.Warn($"[CloudHttp] Skip protected request because no cloud token is available yet. Waiting for edge-login. Url:{request.RequestUri}");
            return false;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    private bool IsAnonymousRequest(HttpRequestMessage request)
    {
        if (!AnonymousMethods.Contains(request.Method))
        {
            return false;
        }

        var requestPath = request.RequestUri?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            return false;
        }

        return string.Equals(requestPath, GetEndpointPath(_endpointProvider.GetDeviceInstancePath()), StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestPath, GetEndpointPath(_endpointProvider.GetIdentityDeviceLoginPath()), StringComparison.OrdinalIgnoreCase);
    }

    private string GetEndpointPath(string endpoint)
        => new Uri(_endpointProvider.BuildUrl(endpoint), UriKind.Absolute).AbsolutePath;

    private static object SanitizePayload(object payload)
    {
        JsonNode? node;

        try
        {
            node = JsonSerializer.SerializeToNode(payload);
        }
        catch
        {
            return payload;
        }

        if (node is null)
        {
            return payload;
        }

        RemoveBlockedKeysRecursively(node);
        return node;
    }

    private static void RemoveBlockedKeysRecursively(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keysToRemove = obj
                .Select(kv => kv.Key)
                .Where(k => BlockedIdentityKeys.Contains(k))
                .ToList();

            foreach (var key in keysToRemove)
            {
                obj.Remove(key);
            }

            foreach (var kv in obj.ToList())
            {
                if (kv.Value is not null)
                {
                    RemoveBlockedKeysRecursively(kv.Value);
                }
            }

            return;
        }

        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    RemoveBlockedKeysRecursively(item);
                }
            }
        }
    }
}
