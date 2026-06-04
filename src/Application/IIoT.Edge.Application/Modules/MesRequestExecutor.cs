using System.Globalization;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Modules;

public sealed class MesRequestExecutor
{
    private readonly IMesHttpClient _mesHttpClient;
    private readonly IMesEndpointProvider _endpointProvider;
    private readonly IModuleParamRoleProvider _moduleParamRoleProvider;
    private readonly ILogService _logger;

    public MesRequestExecutor(
        IMesHttpClient mesHttpClient,
        IMesEndpointProvider endpointProvider,
        IModuleParamRoleProvider moduleParamRoleProvider,
        ILogService logger)
    {
        _mesHttpClient = mesHttpClient;
        _endpointProvider = endpointProvider;
        _moduleParamRoleProvider = moduleParamRoleProvider;
        _logger = logger;
    }

    public async Task<MesCallResult> ExecuteAsync(
        string processType,
        DeviceSession? device,
        string relativePath,
        Func<DeviceSession, CancellationToken, Task<object>> payloadFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(payloadFactory);

        return await ExecuteMesCallCoreAsync(
                processType,
                relativePath,
                () => device is null
                    ? MesCallResult.InvalidContext("设备尚未完成云端身份初始化。")
                    : null,
                async () =>
                {
                    var payload = await payloadFactory(device!, cancellationToken).ConfigureAwait(false);
                    return await _mesHttpClient
                        .PostWithResponseAsync(processType, relativePath, payload, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                },
                response => ParseResponse(relativePath, response),
                static result => result,
                MesCallResult.TransportFailure,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MesCallResult<TData>> ExecuteGetAsync<TData>(
        string processType,
        string relativePath,
        IReadOnlyDictionary<string, string?> query,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dataParser);

        var url = BuildRelativeUrl(relativePath, query);
        return await ExecuteMesCallCoreAsync(
                processType,
                relativePath,
                static () => null,
                () => _mesHttpClient.GetAsync(processType, url, cancellationToken: cancellationToken),
                response => ParseResponse(relativePath, response, dataParser),
                ToTypedPreflight<TData>,
                MesCallResult<TData>.TransportFailure,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MesCallResult<TData>> ExecutePostAsync<TData>(
        string processType,
        string relativePath,
        object payload,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(dataParser);

        return await ExecuteMesCallCoreAsync(
                processType,
                relativePath,
                static () => null,
                () => _mesHttpClient.PostWithResponseAsync(processType, relativePath, payload, cancellationToken: cancellationToken),
                response => ParseResponse(relativePath, response, dataParser),
                ToTypedPreflight<TData>,
                MesCallResult<TData>.TransportFailure,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TResult> ExecuteMesCallCoreAsync<TResult>(
        string processType,
        string relativePath,
        Func<TResult?> validateContext,
        Func<Task<string?>> httpCall,
        Func<string, TResult> parseResponse,
        Func<MesCallResult, TResult> preflightConverter,
        Func<string, TResult> transportFailure,
        CancellationToken cancellationToken)
        where TResult : class
    {
        var preflight = await CheckCommonPreflightAsync(processType, cancellationToken).ConfigureAwait(false);
        if (preflight is not null)
        {
            return preflightConverter(preflight);
        }

        var contextFailure = validateContext();
        if (contextFailure is not null)
        {
            return contextFailure;
        }

        try
        {
            var response = await httpCall().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(response)
                ? transportFailure("MES 返回空响应。")
                : parseResponse(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[MES] 调用接口 {relativePath} 失败：{ex.Message}");
            return transportFailure($"MES 调用异常：{ex.Message}");
        }
    }

    private async Task<MesCallResult?> CheckCommonPreflightAsync(
        string processType,
        CancellationToken cancellationToken)
    {
        var mesEnabled = await _moduleParamRoleProvider
            .GetMesBoolAsync(
                processType,
                ModuleParamRole.MesEnabled,
                defaultValue: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (!mesEnabled)
        {
            return MesCallResult.Disabled("MES 上传已被配置关闭。");
        }

        if (!await _endpointProvider.IsConfiguredAsync(processType, cancellationToken).ConfigureAwait(false))
        {
            return MesCallResult.InvalidContext("MES 基础地址未配置。");
        }

        return null;
    }

    private MesCallResult ParseResponse(string relativePath, string response)
        => ParseResponseCore(
            relativePath,
            response,
            (_, code, message) =>
            {
                if (code == 200)
                {
                    return MesCallResult.Success(
                        string.IsNullOrWhiteSpace(message) ? "MES 调用成功。" : message);
                }

                return MesCallResult.BusinessRejected(
                    string.IsNullOrWhiteSpace(message)
                        ? $"MES 拒绝接口 {relativePath}，返回码：{code}。"
                        : message);
            },
            MesCallResult.TransportFailure);

    private MesCallResult<TData> ParseResponse<TData>(
        string relativePath,
        string response,
        Func<JsonElement, TData> dataParser)
        => ParseResponseCore(
            relativePath,
            response,
            (root, code, message) =>
            {
                if (code != 200)
                {
                    return MesCallResult<TData>.BusinessRejected(
                        string.IsNullOrWhiteSpace(message)
                            ? $"MES 拒绝接口 {relativePath}，返回码：{code}。"
                            : message);
                }

                if (!root.TryGetProperty("data", out var dataElement)
                    || dataElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return MesCallResult<TData>.TransportFailure($"MES 接口 {relativePath} 响应缺少 data。");
                }

                var data = dataParser(dataElement);
                return MesCallResult<TData>.Success(
                    data,
                    string.IsNullOrWhiteSpace(message) ? "MES 调用成功。" : message);
            },
            MesCallResult<TData>.TransportFailure);

    private TResult ParseResponseCore<TResult>(
        string relativePath,
        string response,
        Func<JsonElement, int, string, TResult> createResult,
        Func<string, TResult> transportFailure)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            var code = TryReadCode(root);
            var message = TryReadMessage(root);
            return createResult(root, code, message);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[MES] 解析接口 {relativePath} 响应失败：{ex.Message}");
            return transportFailure($"MES 响应解析失败：{ex.Message}");
        }
    }

    private static int TryReadCode(JsonElement root)
    {
        if (!root.TryGetProperty("code", out var codeElement))
        {
            return -1;
        }

        return codeElement.ValueKind switch
        {
            JsonValueKind.Number when codeElement.TryGetInt32(out var numericCode) => numericCode,
            JsonValueKind.String when int.TryParse(
                codeElement.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedCode) => parsedCode,
            _ => -1
        };
    }

    private static string TryReadMessage(JsonElement root)
        => root.TryGetProperty("msg", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;

    private static MesCallResult<TData> ToTypedPreflight<TData>(MesCallResult result)
        => result.Outcome switch
        {
            MesCallOutcome.Disabled => MesCallResult<TData>.Disabled(result.Message),
            MesCallOutcome.InvalidContext => MesCallResult<TData>.InvalidContext(result.Message),
            MesCallOutcome.BusinessRejected => MesCallResult<TData>.BusinessRejected(result.Message),
            MesCallOutcome.TransportFailure => MesCallResult<TData>.TransportFailure(result.Message),
            _ => MesCallResult<TData>.Success(default, result.Message)
        };

    private static string BuildRelativeUrl(
        string relativePath,
        IReadOnlyDictionary<string, string?> query)
    {
        if (query.Count == 0)
        {
            return relativePath;
        }

        var separator = relativePath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var pairs = query
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
            .Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value ?? string.Empty)}");

        return relativePath + separator + string.Join("&", pairs);
    }
}
