using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace IIoT.Edge.Infrastructure.Integration.Http;

public sealed class MesHttpClient : IMesHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMesEndpointProvider _endpointProvider;
    private readonly ILogService _logger;

    public MesHttpClient(
        IHttpClientFactory httpClientFactory,
        IMesEndpointProvider endpointProvider,
        ILogService logger)
    {
        _httpClientFactory = httpClientFactory;
        _endpointProvider = endpointProvider;
        _logger = logger;
    }

    public async Task<bool> PostAsync(
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
                HttpMethod.Post,
                url,
                JsonContent.Create(payload),
                headers,
                cancellationToken)
            .ConfigureAwait(false);

        return response.isSuccess;
    }

    public async Task<string?> PostWithResponseAsync(
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
                HttpMethod.Post,
                url,
                JsonContent.Create(payload),
                headers,
                cancellationToken)
            .ConfigureAwait(false);

        return response.content;
    }

    public async Task<string?> GetAsync(
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
                HttpMethod.Get,
                url,
                content: null,
                headers,
                cancellationToken)
            .ConfigureAwait(false);

        return response.content;
    }

    private async Task<(bool isSuccess, string? content)> SendAsync(
        HttpMethod method,
        string url,
        HttpContent? content,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        var requestUrl = url;

        try
        {
            var client = _httpClientFactory.CreateClient("MesApi");
            requestUrl = _endpointProvider.BuildUrl(url);
            using var request = new HttpRequestMessage(method, requestUrl)
            {
                Content = content
            };

            ApplyHeaders(request, _endpointProvider.GetDefaultHeaders());
            ApplyHeaders(request, headers);

            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            _logger.Warn($"[MesHttp] {method} failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return (false, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.Error($"[MesHttp] {method} exception: {requestUrl}, {ex.Message}");
            return (false, null);
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var pair in headers)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.Equals(pair.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content is not null && MediaTypeHeaderValue.TryParse(pair.Value, out var mediaType))
                {
                    request.Content.Headers.ContentType = mediaType;
                }

                continue;
            }

            request.Headers.Remove(pair.Key);
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
    }
}
