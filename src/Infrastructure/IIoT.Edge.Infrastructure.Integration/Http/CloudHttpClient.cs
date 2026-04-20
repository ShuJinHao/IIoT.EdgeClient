using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Integration.Config;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Infrastructure.Integration.Http;

/// <summary>
/// Cloud HTTP client.
/// Wraps auth refresh/retry behavior and returns a stable result model.
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
    private readonly IDeviceAccessTokenProvider _tokenProvider;
    private readonly IDeviceService _deviceService;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly ILogService _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public CloudHttpClient(
        IHttpClientFactory httpClientFactory,
        IDeviceAccessTokenProvider tokenProvider,
        IDeviceService deviceService,
        ICloudApiEndpointProvider endpointProvider,
        ILogService logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _deviceService = deviceService;
        _endpointProvider = endpointProvider;
        _logger = logger;
    }

    public async Task<CloudCallResult> PostAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        var requestUrl = _endpointProvider.BuildUrl(url);

        try
        {
            var sanitizedPayload = SanitizePayload(payload);
            var sendResult = await SendWithRetryAsync(
                HttpMethod.Post,
                requestUrl,
                () => JsonContent.Create(sanitizedPayload),
                options).ConfigureAwait(false);

            if (sendResult.ShortCircuitResult is not null)
            {
                return sendResult.ShortCircuitResult;
            }

            using var response = sendResult.Response!;
            if (response.IsSuccessStatusCode)
            {
                return CloudCallResult.Success();
            }

            _logger.Warn($"[CloudHttp] POST failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return CloudCallResult.Failure(
                CloudCallOutcome.HttpFailure,
                BuildHttpReasonCode(response.StatusCode),
                response.StatusCode);
        }
        catch (TaskCanceledException ex)
        {
            _logger.Warn($"[CloudHttp] POST timeout: {requestUrl}, {ex.Message}");
            return CloudCallResult.Failure(CloudCallOutcome.NetworkFailure, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"[CloudHttp] POST network exception: {requestUrl}, {ex.Message}");
            return CloudCallResult.Failure(CloudCallOutcome.NetworkFailure, "network_exception");
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] POST exception: {requestUrl}, {ex.Message}");
            return CloudCallResult.Failure(CloudCallOutcome.Exception, "exception");
        }
    }

    public async Task<CloudCallResult<string>> PostWithResponseAsync(string url, object payload, CloudRequestOptions? options = null)
    {
        var requestUrl = _endpointProvider.BuildUrl(url);

        try
        {
            var sanitizedPayload = SanitizePayload(payload);
            var sendResult = await SendWithRetryAsync(
                HttpMethod.Post,
                requestUrl,
                () => JsonContent.Create(sanitizedPayload),
                options).ConfigureAwait(false);

            if (sendResult.ShortCircuitResult is not null)
            {
                return ToTypedResult<string>(sendResult.ShortCircuitResult);
            }

            using var response = sendResult.Response!;
            if (response.IsSuccessStatusCode)
            {
                return CloudCallResult<string>.Success(
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            _logger.Warn($"[CloudHttp] POST-with-response failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return CloudCallResult<string>.Failure(
                CloudCallOutcome.HttpFailure,
                BuildHttpReasonCode(response.StatusCode),
                response.StatusCode);
        }
        catch (TaskCanceledException ex)
        {
            _logger.Warn($"[CloudHttp] POST-with-response timeout: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"[CloudHttp] POST-with-response network exception: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "network_exception");
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] POST-with-response exception: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.Exception, "exception");
        }
    }

    public async Task<CloudCallResult<string>> GetAsync(string url, CloudRequestOptions? options = null)
    {
        var requestUrl = _endpointProvider.BuildUrl(url);

        try
        {
            var sendResult = await SendWithRetryAsync(
                HttpMethod.Get,
                requestUrl,
                contentFactory: null,
                options).ConfigureAwait(false);

            if (sendResult.ShortCircuitResult is not null)
            {
                return ToTypedResult<string>(sendResult.ShortCircuitResult);
            }

            using var response = sendResult.Response!;
            if (response.IsSuccessStatusCode)
            {
                return CloudCallResult<string>.Success(
                    await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            _logger.Warn($"[CloudHttp] GET failed: {requestUrl}, Status={(int)response.StatusCode} {response.ReasonPhrase}");
            return CloudCallResult<string>.Failure(
                CloudCallOutcome.HttpFailure,
                BuildHttpReasonCode(response.StatusCode),
                response.StatusCode);
        }
        catch (TaskCanceledException ex)
        {
            _logger.Warn($"[CloudHttp] GET timeout: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.Error($"[CloudHttp] GET network exception: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.NetworkFailure, "network_exception");
        }
        catch (Exception ex)
        {
            _logger.Error($"[CloudHttp] GET exception: {requestUrl}, {ex.Message}");
            return CloudCallResult<string>.Failure(CloudCallOutcome.Exception, "exception");
        }
    }

    private async Task<SendWithRetryResult> SendWithRetryAsync(
        HttpMethod method,
        string requestUrl,
        Func<HttpContent?>? contentFactory,
        CloudRequestOptions? options)
    {
        var isAnonymous = IsAnonymousRequest(method, requestUrl);
        var client = _httpClientFactory.CreateClient("CloudApi");

        using var firstRequest = CreateRequest(method, requestUrl, contentFactory, options);
        var authPreparation = await PrepareAuthorizationAsync(firstRequest, isAnonymous).ConfigureAwait(false);
        if (authPreparation is not null)
        {
            return SendWithRetryResult.WithShortCircuit(authPreparation);
        }

        var firstResponse = await client.SendAsync(firstRequest).ConfigureAwait(false);
        if (isAnonymous || firstResponse.StatusCode != HttpStatusCode.Unauthorized)
        {
            return SendWithRetryResult.WithResponse(firstResponse);
        }

        firstResponse.Dispose();
        _logger.Warn(
            $"event=edge.upload.auth.retry_after_401 route={GetRequestRoute(requestUrl)} status_code=401 result=retry");
        await RefreshBootstrapSingleFlightAsync(firstRequest.Headers.Authorization?.Parameter).ConfigureAwait(false);

        using var retryRequest = CreateRequest(method, requestUrl, contentFactory, options);
        authPreparation = await PrepareAuthorizationAsync(retryRequest, isAnonymous).ConfigureAwait(false);
        if (authPreparation is not null)
        {
            return SendWithRetryResult.WithShortCircuit(authPreparation);
        }

        var retryResponse = await client.SendAsync(retryRequest).ConfigureAwait(false);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            retryResponse.Dispose();
            _deviceService.MarkUploadGateBlocked(
                EdgeUploadBlockReason.UploadTokenRejected,
                DateTimeOffset.UtcNow);
            return SendWithRetryResult.WithShortCircuit(
                CloudCallResult.Failure(
                    CloudCallOutcome.UnauthorizedAfterRetry,
                    "unauthorized_after_retry",
                    HttpStatusCode.Unauthorized));
        }

        return SendWithRetryResult.WithResponse(retryResponse);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string requestUrl,
        Func<HttpContent?>? contentFactory,
        CloudRequestOptions? options)
    {
        var request = new HttpRequestMessage(method, requestUrl);
        if (contentFactory is not null)
        {
            request.Content = contentFactory();
        }

        if (!string.IsNullOrWhiteSpace(options?.IdempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", options.IdempotencyKey);
        }

        return request;
    }

    private async Task<CloudCallResult?> PrepareAuthorizationAsync(
        HttpRequestMessage request,
        bool isAnonymousRequest)
    {
        if (isAnonymousRequest)
        {
            return null;
        }

        if (TryAttachAuthorization(request))
        {
            return null;
        }

        var route = GetRequestRoute(request.RequestUri);
        var refreshReason = ResolveUploadNotReadyReasonCode();
        _logger.Info(
            $"event=edge.upload.auth.refresh_before_send route={route} reason={refreshReason} result=attempt");

        await RefreshBootstrapSingleFlightAsync(_tokenProvider.AccessToken).ConfigureAwait(false);
        if (TryAttachAuthorization(request))
        {
            return null;
        }

        var finalReason = ResolveUploadNotReadyReasonCode();
        _logger.Warn(
            $"event=edge.upload.auth.skip_no_valid_token route={route} reason={finalReason} expires_at_utc={FormatTimestamp(_tokenProvider.AccessTokenExpiresAtUtc)} result=skipped");
        return CloudCallResult.Failure(
            CloudCallOutcome.SkippedUploadNotReady,
            finalReason);
    }

    private bool TryAttachAuthorization(HttpRequestMessage request)
    {
        var token = _tokenProvider.AccessToken?.Trim();
        var expiresAtUtc = _tokenProvider.AccessTokenExpiresAtUtc;
        if (string.IsNullOrWhiteSpace(token)
            || (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow))
        {
            return false;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    private async Task RefreshBootstrapSingleFlightAsync(string? knownToken)
    {
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var currentToken = _tokenProvider.AccessToken?.Trim();
            if (HasUsableToken()
                && !string.IsNullOrWhiteSpace(currentToken)
                && !string.Equals(currentToken, knownToken?.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            await _deviceService.RefreshBootstrapAsync().ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private bool HasUsableToken()
    {
        var token = _tokenProvider.AccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var expiresAtUtc = _tokenProvider.AccessTokenExpiresAtUtc;
        return !expiresAtUtc.HasValue || expiresAtUtc.Value > DateTimeOffset.UtcNow;
    }

    private string ResolveUploadNotReadyReasonCode()
    {
        var gate = _deviceService.CurrentUploadGate;
        if (gate.State == EdgeUploadGateState.Blocked)
        {
            return gate.Reason.ToReasonCode();
        }

        return gate.State == EdgeUploadGateState.Refreshing
            ? "bootstrap_refreshing"
            : ResolveTokenStateReasonCode(_tokenProvider.AccessToken, _tokenProvider.AccessTokenExpiresAtUtc);
    }

    private static string ResolveTokenStateReasonCode(string? token, DateTimeOffset? expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return EdgeUploadBlockReason.MissingUploadToken.ToReasonCode();
        }

        return expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTimeOffset.UtcNow
            ? EdgeUploadBlockReason.ExpiredUploadToken.ToReasonCode()
            : "valid_token";
    }

    private static CloudCallResult<T> ToTypedResult<T>(CloudCallResult result)
        => CloudCallResult<T>.Failure(result.Outcome, result.ReasonCode, result.HttpStatusCode);

    private static string BuildHttpReasonCode(HttpStatusCode statusCode)
        => $"http_status_{(int)statusCode}";

    private static string GetRequestRoute(string requestUrl)
        => GetRequestRoute(new Uri(requestUrl, UriKind.Absolute));

    private static string GetRequestRoute(Uri? requestUri)
        => requestUri?.AbsolutePath ?? "unknown";

    private static string FormatTimestamp(DateTimeOffset? value)
        => value?.ToString("O") ?? "null";

    private bool IsAnonymousRequest(HttpMethod method, string requestUrl)
    {
        if (!AnonymousMethods.Contains(method))
        {
            return false;
        }

        var requestPath = GetRequestRoute(requestUrl);
        return string.Equals(requestPath, _endpointProvider.GetDeviceInstancePath(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestPath, _endpointProvider.GetIdentityDeviceLoginPath(), StringComparison.OrdinalIgnoreCase);
    }

    private static object SanitizePayload(object payload)
    {
        var node = JsonSerializer.SerializeToNode(payload);
        if (node is JsonObject obj)
        {
            RemoveIdentityKeys(obj);
            return obj;
        }

        return payload;
    }

    private static void RemoveIdentityKeys(JsonObject obj)
    {
        foreach (var key in BlockedIdentityKeys.ToList())
        {
            obj.Remove(key);
        }
    }

    private sealed class SendWithRetryResult
    {
        public HttpResponseMessage? Response { get; init; }

        public CloudCallResult? ShortCircuitResult { get; init; }

        public static SendWithRetryResult WithResponse(HttpResponseMessage response)
            => new()
            {
                Response = response
            };

        public static SendWithRetryResult WithShortCircuit(CloudCallResult result)
            => new()
            {
                ShortCircuitResult = result
            };
    }
}
