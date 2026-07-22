using System.Net;
using System.Net.Sockets;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using IIoT.Edge.Module.Contracts.Hardware;

namespace IIoT.Edge.Plc.ContractNetworkTests;

public sealed class McPlcServiceNetworkBehaviorTests
{
    [Fact]
    public async Task ReadDataAsync_WhenReadingWords_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(
            request => CreateReadResponse(request, ToBytes((ushort)0x1234, (ushort)0x5678)));
        await using var service = await CreateConnectedServiceAsync(
            server.Port,
            cancellationToken: TestContext.Current.CancellationToken);

        var values = await service.ReadDataAsync<ushort>(
            "D700",
            2,
            TestContext.Current.CancellationToken);

        Assert.Equal([0x1234, 0x5678], values);
        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x04]));
    }

    [Fact]
    public async Task Disconnect_WhenReadIsInFlight_ShouldWaitForReadOperationGate()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new FakeMc3EServer(async (request, cancellationToken) =>
        {
            requestReceived.SetResult();
            await releaseResponse.Task.WaitAsync(cancellationToken);
            return CreateReadResponse(request, ToBytes((ushort)0x1234));
        });
        await using var service = await CreateConnectedServiceAsync(
            server.Port,
            cancellationToken: TestContext.Current.CancellationToken);

        var readTask = service.ReadDataAsync<ushort>(
            "D700",
            1,
            TestContext.Current.CancellationToken);
        await requestReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        var disconnectTask = service.DisconnectAsync(TestContext.Current.CancellationToken);
        Assert.False(disconnectTask.IsCompleted);

        releaseResponse.SetResult();
        var values = await readTask;
        await disconnectTask.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal((ushort)0x1234, Assert.Single(values));
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ReadDataAsync_WhenReadingBits_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(
            request => CreateReadResponse(request, ToBitBytes(true, false, true)));
        await using var service = await CreateConnectedServiceAsync(
            server.Port,
            cancellationToken: TestContext.Current.CancellationToken);

        var values = await service.ReadDataAsync<bool>(
            "R300",
            3,
            TestContext.Current.CancellationToken);

        Assert.Equal([true, false, true], values);
        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x04]));
    }

    [Fact]
    public async Task WriteDataAsync_WhenWritingWords_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(CreateOkResponse);
        await using var service = await CreateConnectedServiceAsync(
            server.Port,
            cancellationToken: TestContext.Current.CancellationToken);

        await service.WriteDataAsync<ushort>(
            "D600",
            [0x1234, 0x5678],
            TestContext.Current.CancellationToken);

        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x14]));
        Assert.True(ContainsSequence(server.LastRequest!, [0x34, 0x12, 0x78, 0x56]));
    }

    [Fact]
    public async Task ReadDataAsync_WhenFrameTypeIsE4_ShouldUseMcpX4ERequestHeader()
    {
        await using var server = new FakeMc3EServer(_ => []);
        await using var service = await CreateConnectedServiceAsync(
            server.Port,
            McPlcFrameType.E4,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ReadDataAsync<ushort>(
            "D700",
            1,
            TestContext.Current.CancellationToken));

        Assert.NotNull(server.LastRequest);
        Assert.Equal(new byte[] { 0x54, 0x00 }, server.LastRequest!.Take(2).ToArray());
    }

    private static async Task<McPlcService> CreateConnectedServiceAsync(
        int port,
        McPlcFrameType frameType = McPlcFrameType.E3,
        CancellationToken cancellationToken = default)
    {
        var service = new McPlcService();
        service.Init(new TcpPlcEndpoint("127.0.0.1", port, 1000, frameType));
        Assert.True(await service.ConnectAsync(cancellationToken));
        return service;
    }

    private static byte[] CreateReadResponse(byte[] request, byte[] content)
        => CreateResponse(request, content);

    private static byte[] CreateOkResponse(byte[] request)
        => CreateResponse(request, []);

    private static byte[] CreateResponse(byte[] request, byte[] content)
    {
        var route = request.Skip(2).Take(5).ToArray();
        var contentLength = (ushort)(2 + content.Length);

        return
        [
            0xD0, 0x00,
            route[0], route[1], route[2], route[3], route[4],
            (byte)(contentLength & 0xFF), (byte)(contentLength >> 8),
            0x00, 0x00,
            .. content
        ];
    }

    private static byte[] ToBytes(params ushort[] values)
    {
        var bytes = new List<byte>();
        foreach (var value in values)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)(value >> 8));
        }

        return bytes.ToArray();
    }

    private static byte[] ToBitBytes(params bool[] values)
    {
        var bytes = new List<byte>();
        for (var index = 0; index < values.Length; index += 2)
        {
            var value = values[index] ? 0x10 : 0x00;
            if (index + 1 < values.Length && values[index + 1])
            {
                value |= 0x01;
            }

            bytes.Add((byte)value);
        }

        return bytes.ToArray();
    }

    private static bool ContainsSequence(byte[] source, byte[] expected)
    {
        for (var index = 0; index <= source.Length - expected.Length; index++)
        {
            if (source.AsSpan(index, expected.Length).SequenceEqual(expected))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class FakeMc3EServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<byte[], CancellationToken, Task<byte[]>> _responseFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        public FakeMc3EServer(Func<byte[], byte[]> responseFactory)
        {
            _responseFactory = (request, _) => Task.FromResult(responseFactory(request));
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _worker = Task.Run(RunAsync);
        }

        public FakeMc3EServer(Func<byte[], CancellationToken, Task<byte[]>> responseFactory)
        {
            _responseFactory = responseFactory;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _worker = Task.Run(RunAsync);
        }

        public int Port { get; }

        public byte[]? LastRequest { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _cts.Dispose();
            }
        }

        private async Task RunAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            var count = await stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
            if (count <= 0)
            {
                return;
            }

            LastRequest = buffer.Take(count).ToArray();
            var response = await _responseFactory(LastRequest, _cts.Token).ConfigureAwait(false);
            if (response.Length > 0)
            {
                await stream.WriteAsync(response, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
        }
    }
}
