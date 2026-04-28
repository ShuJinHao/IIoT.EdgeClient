using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class PlcServiceProxyBehaviorTests
{
    [Fact]
    public async Task ConnectAsync_WhenConnectionFails_ShouldLogReadableWarning()
    {
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                ConnectAsyncHandler = () => Task.FromResult(false)
            },
            logger,
            "PLC-A");

        var connected = await proxy.ConnectAsync();

        Assert.False(connected);
        Assert.Contains(logger.Entries, x => x.Message == "[PLC-A] 连接失败");
    }

    [Fact]
    public async Task ConnectAsync_WhenConnectionThrows_ShouldLogReadableError()
    {
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                ConnectAsyncHandler = () => throw new InvalidOperationException("network down")
            },
            logger,
            "PLC-A");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.ConnectAsync());

        Assert.Equal("network down", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PLC-A] 连接异常: network down");
    }

    [Fact]
    public async Task ReadDataAsync_WhenReadThrows_ShouldLogReadableError()
    {
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                ReadAsyncHandler = (_, _) => throw new InvalidOperationException("read failed")
            },
            logger,
            "PLC-A");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.ReadDataAsync<int>("DB1.DBW0", 2));

        Assert.Equal("read failed", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PLC-A] 读取 DB1.DBW0 失败: read failed");
    }

    [Fact]
    public async Task WriteDataAsync_WhenWriteThrows_ShouldLogReadableError()
    {
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                WriteAsyncHandler = (_, _) => throw new InvalidOperationException("write failed")
            },
            logger,
            "PLC-A");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.WriteDataAsync("DB1.DBW0", [1, 2]));

        Assert.Equal("write failed", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PLC-A] 写入 DB1.DBW0 失败: write failed");
    }

    private sealed class FakePlcService : IPlcService
    {
        public Func<Task<bool>>? ConnectAsyncHandler { get; init; }

        public Func<string, ushort, Task>? ReadAsyncHandler { get; init; }

        public Func<string, object, Task>? WriteAsyncHandler { get; init; }

        public bool IsConnected => false;

        public void Init(string ip, int port)
        {
        }

        public Task<bool> ConnectAsync()
            => ConnectAsyncHandler?.Invoke() ?? Task.FromResult(true);

        public void Disconnect()
        {
        }

        public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
        {
            if (ReadAsyncHandler is not null)
            {
                await ReadAsyncHandler(address, length);
            }

            return [];
        }

        public Task WriteDataAsync<T>(string address, List<T> data)
            => WriteAsyncHandler?.Invoke(address, data) ?? Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
