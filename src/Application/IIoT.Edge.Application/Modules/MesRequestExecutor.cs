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

        var mesEnabled = await _moduleParamRoleProvider
            .GetBoolAsync(
                processType,
                ModuleParamCategory.Mes,
                ModuleParamRole.MesEnabled,
                defaultValue: false,
                cancellationToken)
            .ConfigureAwait(false);
        if (!mesEnabled)
        {
            return MesCallResult.Disabled("MES 上传已被配置关闭。");
        }

        if (device is null)
        {
            return MesCallResult.InvalidContext("设备尚未完成云端身份初始化。");
        }

        if (!await _endpointProvider.IsConfiguredAsync(processType, cancellationToken).ConfigureAwait(false))
        {
            return MesCallResult.InvalidContext("MES 基础地址未配置。");
        }

        try
        {
            var payload = await payloadFactory(device, cancellationToken).ConfigureAwait(false);
            var response = await _mesHttpClient
                .PostWithResponseAsync(processType, relativePath, payload, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
            {
                return MesCallResult.TransportFailure("MES 返回空响应。");
            }

            return ParseResponse(relativePath, response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[MES] 调用接口 {relativePath} 失败：{ex.Message}");
            return MesCallResult.TransportFailure($"MES 调用异常：{ex.Message}");
        }
    }

    private MesCallResult ParseResponse(string relativePath, string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            var code = TryReadCode(root);
            var message = root.TryGetProperty("msg", out var messageElement)
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            if (code == 200)
            {
                return MesCallResult.Success(
                    string.IsNullOrWhiteSpace(message) ? "MES 调用成功。" : message);
            }

            return MesCallResult.BusinessRejected(
                string.IsNullOrWhiteSpace(message)
                    ? $"MES 拒绝接口 {relativePath}，返回码：{code}。"
                    : message);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[MES] 解析接口 {relativePath} 响应失败：{ex.Message}");
            return MesCallResult.TransportFailure($"MES 响应解析失败：{ex.Message}");
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
            JsonValueKind.String when int.TryParse(codeElement.GetString(), out var parsedCode) => parsedCode,
            _ => -1
        };
    }
}
