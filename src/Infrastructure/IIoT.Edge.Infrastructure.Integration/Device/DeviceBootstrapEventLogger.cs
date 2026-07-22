using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Common.Device;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Infrastructure.Integration.Device;

public interface IDeviceBootstrapEventLogger
{
    void LogSessionAccepted(string eventName, DeviceSession session);

    void LogSessionRejected(string eventName, DeviceSession session, EdgeUploadBlockReason reason);

    void LogBootstrapFailure(CloudDeviceBootstrapResult result);

    void LogRefreshFailure(CloudDeviceBootstrapResult result);
}

public sealed class DeviceBootstrapEventLogger : IDeviceBootstrapEventLogger
{
    private readonly ILogService _logger;

    public DeviceBootstrapEventLogger(ILogService logger)
    {
        _logger = logger;
    }

    public void LogSessionAccepted(string eventName, DeviceSession session)
        => _logger.Info(
            $"事件({eventName}) 客户端编码={FormatValue(session.ClientCode)} 设备ID={session.DeviceId} 工序ID={session.ProcessId} 令牌过期时间={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} 结果=成功");

    public void LogSessionRejected(string eventName, DeviceSession session, EdgeUploadBlockReason reason)
        => _logger.Warn(
            $"事件({eventName}) 客户端编码={FormatValue(session.ClientCode)} 设备ID={session.DeviceId} 工序ID={session.ProcessId} 令牌过期时间={FormatTimestamp(session.UploadAccessTokenExpiresAtUtc)} 结果=无效 原因={reason.ToReasonCode()}");

    public void LogBootstrapFailure(CloudDeviceBootstrapResult result)
        => LogFailure("edge.bootstrap.failure", result);

    public void LogRefreshFailure(CloudDeviceBootstrapResult result)
        => LogFailure("edge.bootstrap.refresh.failure", result);

    private void LogFailure(string eventName, CloudDeviceBootstrapResult result)
    {
        switch (result.Kind)
        {
            case CloudDeviceBootstrapResultKind.HttpFailure:
                _logger.Warn(
                    $"事件({eventName}) 客户端编码={FormatValue(result.ClientCode)} 状态码={result.StatusCode.GetValueOrDefault()} 结果=失败 原因=HTTP状态 错误={FormatValue(result.ErrorMessage)}");
                break;
            case CloudDeviceBootstrapResultKind.EmptyPayload:
                _logger.Warn(
                    $"事件({eventName}) 客户端编码={FormatValue(result.ClientCode)} 结果=失败 原因=空响应");
                break;
            case CloudDeviceBootstrapResultKind.Timeout:
                _logger.Warn(
                    $"事件({eventName}) 客户端编码={FormatValue(result.ClientCode)} 结果=失败 原因=超时");
                break;
            case CloudDeviceBootstrapResultKind.NetworkFailure:
                _logger.Warn(
                    $"事件({eventName}) 客户端编码={FormatValue(result.ClientCode)} 结果=失败 原因=网络异常 消息={SanitizeValue(result.ErrorMessage ?? string.Empty)}");
                break;
            default:
                _logger.Error(
                    $"事件({eventName}) 客户端编码={FormatValue(result.ClientCode)} 结果=失败 原因=异常 消息={SanitizeValue(result.ErrorMessage ?? string.Empty)}");
                break;
        }
    }

    private static string FormatTimestamp(DateTimeOffset? value)
        => value?.ToString("O") ?? "空";

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未知" : SanitizeValue(value);

    private static string SanitizeValue(string value)
        => value.Replace(' ', '_');
}
