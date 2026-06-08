using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.CloudClient;

public interface ICloudClientHttpTransport
{
    Task<EdgeCloudOperationResult<T>> GetJsonAsync<T>(
        Uri uri,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default);

    Task<EdgeCloudOperationResult> PostJsonAsync<TBody>(
        Uri uri,
        TBody body,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default);

    Task<EdgeCloudOperationResult<TResponse>> PostJsonAsync<TBody, TResponse>(
        Uri uri,
        TBody? body,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default);
}

public sealed class CloudClientHttpTransport : ICloudClientHttpTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<HttpClient> _httpClientFactory;

    public CloudClientHttpTransport(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClientFactory = () => httpClient;
    }

    public CloudClientHttpTransport(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<EdgeCloudOperationResult<T>> GetJsonAsync<T>(
        Uri uri,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        configureHeaders?.Invoke(request.Headers);
        return await SendJsonAsync<T>(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EdgeCloudOperationResult> PostJsonAsync<TBody>(
        Uri uri,
        TBody body,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        configureHeaders?.Invoke(request.Headers);
        return await SendAsync(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EdgeCloudOperationResult<TResponse>> PostJsonAsync<TBody, TResponse>(
        Uri uri,
        TBody? body,
        TimeSpan? timeout,
        Action<HttpRequestHeaders>? configureHeaders = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        configureHeaders?.Invoke(request.Headers);
        return await SendJsonAsync<TResponse>(request, timeout, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EdgeCloudOperationResult<T>> SendJsonAsync<T>(
        HttpRequestMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendCoreAsync(request, timeout, cancellationToken).ConfigureAwait(false);
            var headers = ReadHeaders(response);
            if (!response.IsSuccessStatusCode)
            {
                return EdgeCloudOperationResult<T>.Failed(
                    EdgeCloudFailureKind.HttpFailure,
                    await TryReadFirstErrorAsync(response, cancellationToken).ConfigureAwait(false),
                    (int)response.StatusCode,
                    headers);
            }

            var value = await response.Content
                .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return value is null
                ? EdgeCloudOperationResult<T>.Failed(EdgeCloudFailureKind.EmptyPayload, "Cloud 返回空响应。", headers: headers)
                : EdgeCloudOperationResult<T>.Succeeded(value, headers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return EdgeCloudOperationResult<T>.Failed(EdgeCloudFailureKind.Cancelled, "Cloud 请求已取消。");
        }
        catch (OperationCanceledException)
        {
            return EdgeCloudOperationResult<T>.Failed(EdgeCloudFailureKind.Timeout, "Cloud 请求超时。");
        }
        catch (HttpRequestException ex)
        {
            return EdgeCloudOperationResult<T>.Failed(EdgeCloudFailureKind.NetworkFailure, ex.Message);
        }
        catch (Exception ex)
        {
            return EdgeCloudOperationResult<T>.Failed(EdgeCloudFailureKind.UnexpectedFailure, ex.Message);
        }
    }

    private async Task<EdgeCloudOperationResult> SendAsync(
        HttpRequestMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendCoreAsync(request, timeout, cancellationToken).ConfigureAwait(false);
            var headers = ReadHeaders(response);
            return response.IsSuccessStatusCode
                ? EdgeCloudOperationResult.Succeeded(headers)
                : EdgeCloudOperationResult.Failed(
                    EdgeCloudFailureKind.HttpFailure,
                    await TryReadFirstErrorAsync(response, cancellationToken).ConfigureAwait(false),
                    (int)response.StatusCode,
                    headers);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return EdgeCloudOperationResult.Failed(EdgeCloudFailureKind.Cancelled, "Cloud 请求已取消。");
        }
        catch (OperationCanceledException)
        {
            return EdgeCloudOperationResult.Failed(EdgeCloudFailureKind.Timeout, "Cloud 请求超时。");
        }
        catch (HttpRequestException ex)
        {
            return EdgeCloudOperationResult.Failed(EdgeCloudFailureKind.NetworkFailure, ex.Message);
        }
        catch (Exception ex)
        {
            return EdgeCloudOperationResult.Failed(EdgeCloudFailureKind.UnexpectedFailure, ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpRequestMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (timeout is null || timeout.Value <= TimeSpan.Zero)
        {
            return await _httpClientFactory()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout.Value);
        return await _httpClientFactory()
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
            .ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> ReadHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = header.Value.FirstOrDefault() ?? string.Empty;
        }

        return headers;
    }

    private static async Task<string?> TryReadFirstErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var first = errors.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String)
                {
                    return first.GetString();
                }
            }
        }
        catch
        {
        }

        return $"Cloud 请求失败: HTTP {(int)response.StatusCode}";
    }
}
