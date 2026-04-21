namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 本地参数配置变更发布入口。
/// 保存链路在持久化和缓存失效完成后通过它通知本地消费者刷新。
/// </summary>
public interface ILocalParameterConfigChangePublisher
{
    void NotifySystemChanged();

    void NotifyDeviceChanged(int deviceId);
}
