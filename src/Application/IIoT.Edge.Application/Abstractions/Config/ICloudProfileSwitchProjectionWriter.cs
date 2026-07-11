namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 将当前 profile SQLite 中的唯一 Cloud 开关投影给不加载业务数据库的 Launcher。
/// 投影只允许由 Shell 生成，不是第二个配置入口。
/// </summary>
public interface ICloudProfileSwitchProjectionWriter
{
    Task WriteAsync(bool enabled, CancellationToken cancellationToken = default);
}
