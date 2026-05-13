using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Infrastructure.Integration.Device.Cache;

/// <summary>
/// 设备会话缓存协调器，集中处理文件缓存读写异常与日志。
/// </summary>
public interface IDeviceSessionCacheCoordinator
{
    DeviceSession? TryLoad(string clientCode);

    void Save(DeviceSession session);
}

public sealed class DeviceSessionCacheCoordinator(
    IDeviceSessionCacheStore cacheStore,
    ILogService logger) : IDeviceSessionCacheCoordinator
{
    public DeviceSession? TryLoad(string clientCode)
    {
        try
        {
            var cached = cacheStore.TryLoad(clientCode);
            if (cached is not null)
            {
                logger.Info($"[设备服务] 已加载本地缓存：{cached.DeviceName}");
            }

            return cached;
        }
        catch (Exception ex)
        {
            logger.Warn($"[设备服务] 加载本地缓存失败：{ex.Message}");
            return null;
        }
    }

    public void Save(DeviceSession session)
    {
        try
        {
            cacheStore.Save(session);
        }
        catch (Exception ex)
        {
            logger.Warn($"[设备服务] 保存本地缓存失败：{ex.Message}");
        }
    }
}
