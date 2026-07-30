using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

namespace IIoT.Edge.Plc.ContractTests;

public sealed class PlcServiceProxyBehaviorTests
{
    [Fact]
    public void Init_ShouldForwardEndpointToTarget()
    {
        var target = new FakePlcService();
        var endpoint = new TcpPlcEndpoint("127.0.0.1", 502);
        var proxy = new PlcServiceProxy(target, new FakeLogService(), "PLC-A");

        proxy.Init(endpoint);

        Assert.Same(endpoint, target.Endpoint);
    }

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

        var connected = await proxy.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.False(connected);
        Assert.Contains(logger.Entries, x => x.Message == "[PlcCode=PLC-A] 连接失败");
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => proxy.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.Equal("network down", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PlcCode=PLC-A] 连接异常: network down");
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.ReadDataAsync<int>(
            "DB1.DBW0",
            2,
            TestContext.Current.CancellationToken));

        Assert.Equal("read failed", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PlcCode=PLC-A] 读取 DB1.DBW0 失败: read failed");
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.WriteDataAsync(
            "DB1.DBW0",
            [1, 2],
            TestContext.Current.CancellationToken));

        Assert.Equal("write failed", ex.Message);
        Assert.Contains(logger.Entries, x => x.Message == "[PlcCode=PLC-A] 写入 DB1.DBW0 失败: write failed");
    }

    [Fact]
    public async Task ConnectAsync_WhenCanceled_ShouldPropagateWithoutErrorLog()
    {
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                ConnectAsyncHandler = () => throw new OperationCanceledException("caller canceled")
            },
            logger,
            "PLC-A");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => proxy.ConnectAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == "Error");
    }

    [Fact]
    public async Task ReadDataAsync_WhenServiceQuarantined_ShouldPreserveStableException()
    {
        var expected = new PlcServiceQuarantinedException(
            "FakePLC",
            "Read",
            "protocol did not settle");
        var logger = new FakeLogService();
        var proxy = new PlcServiceProxy(
            new FakePlcService
            {
                ReadAsyncHandler = (_, _) => throw expected
            },
            logger,
            "PLC-A");

        var actual = await Assert.ThrowsAsync<PlcServiceQuarantinedException>(
            () => proxy.ReadDataAsync<ushort>(
                "D100",
                1,
                TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
        Assert.Equal(PlcServiceQuarantinedException.StableReasonCode, actual.ReasonCode);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Error"
                     && entry.Message.Contains(PlcServiceQuarantinedException.StableReasonCode, StringComparison.Ordinal));
    }

    private sealed class FakePlcService : PlcServiceTestDouble
    {
        public Func<Task<bool>>? ConnectAsyncHandler { get; init; }

        public Func<string, ushort, Task>? ReadAsyncHandler { get; init; }

        public Func<string, object, Task>? WriteAsyncHandler { get; init; }

        public PlcEndpoint? Endpoint { get; private set; }

        public override bool IsConnected => false;

        public override void Init(PlcEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        public override Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
            => ConnectAsyncHandler?.Invoke() ?? Task.FromResult(true);

        public override async Task<List<T>> ReadDataAsync<T>(
            string address,
            ushort length,
            CancellationToken cancellationToken = default)
        {
            if (ReadAsyncHandler is not null)
            {
                await ReadAsyncHandler(address, length);
            }

            return [];
        }

        public override Task WriteDataAsync<T>(
            string address,
            List<T> data,
            CancellationToken cancellationToken = default)
            => WriteAsyncHandler?.Invoke(address, data) ?? Task.CompletedTask;
    }
}
