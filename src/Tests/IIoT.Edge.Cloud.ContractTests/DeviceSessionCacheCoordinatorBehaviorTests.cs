using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Infrastructure.Integration.Device.Cache;

namespace IIoT.Edge.Cloud.ContractTests;

public sealed class DeviceSessionCacheCoordinatorBehaviorTests
{
    [Fact]
    public void TryLoad_WhenStoreThrows_ShouldReturnNullAndWriteWarning()
    {
        var logger = new FakeLogService();
        var coordinator = new DeviceSessionCacheCoordinator(
            new ThrowingDeviceSessionCacheStore(),
            logger);

        var session = coordinator.TryLoad("LINE-A-01");

        Assert.Null(session);
        Assert.Contains(logger.Entries, x =>
            x.Message.Contains("加载本地缓存失败", StringComparison.Ordinal));
    }

    [Fact]
    public void Save_WhenStoreThrows_ShouldWriteWarning()
    {
        var logger = new FakeLogService();
        var coordinator = new DeviceSessionCacheCoordinator(
            new ThrowingDeviceSessionCacheStore(),
            logger);

        coordinator.Save(new DeviceSession
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "缓存设备",
            ClientCode = "LINE-A-01",
            ProcessId = Guid.NewGuid()
        });

        Assert.Contains(logger.Entries, x =>
            x.Message.Contains("保存本地缓存失败", StringComparison.Ordinal));
    }

    private sealed class ThrowingDeviceSessionCacheStore : IDeviceSessionCacheStore
    {
        public void Save(DeviceSession session)
            => throw new IOException("cache unavailable");

        public DeviceSession? TryLoad(string clientCode)
            => throw new IOException("cache unavailable");
    }
}
