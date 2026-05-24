using System.Net;
using System.Net.Sockets;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class McPlcServiceBehaviorTests
{
    [Theory]
    [InlineData("D700", "D", "700")]
    [InlineData("ZR400", "ZR", "400")]
    [InlineData("R300", "R", "300")]
    [InlineData("x1f", "X", "1F")]
    public void ParseAddress_ShouldSplitPrefixAndDeviceNumber(
        string address,
        string expectedPrefix,
        string expectedDeviceAddress)
    {
        var parsed = McPlcService.ParseAddress(address);

        Assert.Equal(expectedPrefix, parsed.Prefix.ToString());
        Assert.Equal(expectedDeviceAddress, parsed.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("700")]
    [InlineData("DABC")]
    [InlineData("UNKNOWN700")]
    public void ParseAddress_WhenAddressInvalid_ShouldReject(string address)
        => Assert.Throws<FormatException>(() => McPlcService.ParseAddress(address));

    [Fact]
    public async Task ReadDataAsync_WhenReadingWords_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(
            request => CreateReadResponse(request, ToBytes((ushort)0x1234, (ushort)0x5678)));
        using var service = CreateConnectedService(server.Port);

        var values = await service.ReadDataAsync<ushort>("D700", 2);

        Assert.Equal([0x1234, 0x5678], values);
        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x04]));
    }

    [Fact]
    public async Task ReadDataAsync_WhenReadingBits_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(
            request => CreateReadResponse(request, ToBitBytes(true, false, true)));
        using var service = CreateConnectedService(server.Port);

        var values = await service.ReadDataAsync<bool>("R300", 3);

        Assert.Equal([true, false, true], values);
        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x04]));
    }

    [Fact]
    public async Task WriteDataAsync_WhenWritingWords_ShouldUseMcpX3EProtocol()
    {
        await using var server = new FakeMc3EServer(CreateOkResponse);
        using var service = CreateConnectedService(server.Port);

        await service.WriteDataAsync<ushort>("D600", [0x1234, 0x5678]);

        Assert.NotNull(server.LastRequest);
        Assert.True(ContainsSequence(server.LastRequest!, [0x01, 0x14]));
        Assert.True(ContainsSequence(server.LastRequest!, [0x34, 0x12, 0x78, 0x56]));
    }

    [Fact]
    public void PlcServiceFactory_WhenMc_ShouldCreateMcpXBackedMcService()
    {
        var factory = new PlcServiceFactory(new FakeLogService(), new ModbusAddressParser());

        using var service = factory.Create(PlcType.Mc, "PLC-MC");

        var target = typeof(PlcServiceProxy)
            .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service);
        Assert.IsType<McPlcService>(target);
    }

    private static McPlcService CreateConnectedService(int port)
    {
        var service = new McPlcService();
        service.Init(new TcpPlcEndpoint("127.0.0.1", port, 1000));
        Assert.True(service.ConnectAsync().GetAwaiter().GetResult());
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
        private readonly Func<byte[], byte[]> _responseFactory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        public FakeMc3EServer(Func<byte[], byte[]> responseFactory)
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
            var response = _responseFactory(LastRequest);
            if (response.Length > 0)
            {
                await stream.WriteAsync(response, _cts.Token).ConfigureAwait(false);
                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
            }
        }
    }
}
