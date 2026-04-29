using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesHeartbeatProbe : IMesHeartbeatProbe
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMesEndpointProvider _endpointProvider;
    private readonly IOptionsMonitor<MesApiConfig> _options;
    private readonly ILogService _logger;

    public MesHeartbeatProbe(
        IHttpClientFactory httpClientFactory,
        IMesEndpointProvider endpointProvider,
        IOptionsMonitor<MesApiConfig> options,
        ILogService logger)
    {
        _httpClientFactory = httpClientFactory;
        _endpointProvider = endpointProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<ExternalHeartbeatSnapshot> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var attemptedAt = DateTime.UtcNow;
        var heartbeatPath = _options.CurrentValue.HeartbeatPath?.Trim();
        if (string.IsNullOrWhiteSpace(heartbeatPath))
        {
            return NotReady("mes_heartbeat_path_missing", "MES 心跳路径未配置。", attemptedAt);
        }

        if (!_endpointProvider.IsConfigured)
        {
            return NotReady("mes_base_url_missing", "MES 基础地址未配置。", attemptedAt);
        }

        var requestUrl = heartbeatPath;
        try
        {
            var client = _httpClientFactory.CreateClient("MesApi");
            requestUrl = _endpointProvider.BuildUrl(heartbeatPath);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            foreach (var header in _endpointProvider.GetDefaultHeaders())
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new ExternalHeartbeatSnapshot(
                    ExternalSystemKind.Mes,
                    ExternalHeartbeatState.Ready,
                    "ready",
                    null,
                    attemptedAt,
                    attemptedAt,
                    null);
            }

            var reason = $"http_{(int)response.StatusCode}";
            _logger.Warn($"[MES] 心跳失败：{requestUrl}，状态码={(int)response.StatusCode}。");
            return NotReady(reason, response.ReasonPhrase, attemptedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.Warn($"[MES] 心跳超时：{requestUrl}，{ex.Message}");
            return NotReady("mes_heartbeat_timeout", ex.Message, attemptedAt);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[MES] 心跳异常：{requestUrl}，{ex.Message}");
            return NotReady("mes_heartbeat_exception", ex.Message, attemptedAt);
        }
    }

    private static ExternalHeartbeatSnapshot NotReady(string reasonCode, string? message, DateTime attemptedAt)
        => new(
            ExternalSystemKind.Mes,
            ExternalHeartbeatState.NotReady,
            reasonCode,
            message,
            attemptedAt,
            null,
            attemptedAt);
}
