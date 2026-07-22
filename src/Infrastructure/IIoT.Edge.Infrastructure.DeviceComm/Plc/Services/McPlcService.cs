using IIoT.Edge.Module.Contracts.Plc;
using McpXLib;
using McpXLib.Enums;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class McPlcService : IPlcService
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OperationSettleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);
    private static readonly HashSet<Prefix> HexAddressPrefixes =
    [
        Prefix.X,
        Prefix.Y,
        Prefix.B,
        Prefix.W,
        Prefix.SB,
        Prefix.SW,
        Prefix.DX,
        Prefix.DY
    ];

    private readonly PlcOperationGate _operationGate = new(
        nameof(McPlcService),
        OperationSettleTimeout,
        DisposeTimeout);
    private PlcTransportOwner<McpX>? _protocolOwner;
    private TcpPlcEndpoint? _endpoint;
    private int _port;
    private bool _isConnected;

    public bool IsConnected => _operationGate.IsOpen && _isConnected && _protocolOwner?.IsAvailable == true;

    public void Init(PlcEndpoint endpoint)
    {
        _operationGate.ThrowIfNotOpen(nameof(Init));
        _endpoint = endpoint as TcpPlcEndpoint
            ?? throw new ArgumentException("MC PLC 只支持 TCP 端点。", nameof(endpoint));
        _port = _endpoint.Port;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new ProtocolOperationContext();
        var endpoint = _endpoint;
        var timeout = endpoint?.ConnectTimeout > TimeSpan.Zero
            ? endpoint.ConnectTimeout
            : OperationTimeout;
        var endpointDisplay = endpoint is null
            ? "<uninitialized>"
            : $"{endpoint.Host}:{endpoint.Port}";
        try
        {
            return await _operationGate.ExecuteAsync(
                    nameof(ConnectAsync),
                    token => Task.Run(() => ConnectCore(context, token), CancellationToken.None),
                    timeout,
                    () => AbortProtocolAsync(context.Owner ?? _protocolOwner),
                    _ => ReleaseProtocolAsync(context.Owner ?? _protocolOwner),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException(
                $"连接 MC PLC {endpointDisplay} 超时，超时时间 {timeout.TotalSeconds:0} 秒。",
                ex);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new ProtocolOperationContext();
        return _operationGate.ExecuteAsync(
            nameof(DisconnectAsync),
            token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                context.Owner = _protocolOwner;
                DisconnectCore();
            }, CancellationToken.None),
            OperationTimeout,
            () => AbortProtocolAsync(context.Owner ?? _protocolOwner),
            _ => ReleaseProtocolAsync(context.Owner ?? _protocolOwner),
            cancellationToken);
    }

    public async Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default)
    {
        var parsedAddress = ParseAddress(address);
        var context = new ProtocolOperationContext();

        try
        {
            return await _operationGate.ExecuteAsync(
                    $"Read {address}",
                    _ =>
                    {
                        context.Owner = EnsureConnected();
                        return ReadSupportedAsync<T>(context.Owner.Value, parsedAddress, length);
                    },
                    OperationTimeout,
                    () => AbortProtocolAsync(context.Owner),
                    _ => ReleaseProtocolAsync(context.Owner),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"读取地址 {address} 超时，超时时间 {OperationTimeout.TotalSeconds:0} 秒。", ex);
        }
        catch (Exception ex) when (PlcOperationGate.ShouldWrapOperationException(ex))
        {
            throw new InvalidOperationException($"读取地址 {address} 失败。", ex);
        }
    }

    public async Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default)
    {
        var parsedAddress = ParseAddress(address);
        var context = new ProtocolOperationContext();

        try
        {
            await _operationGate.ExecuteAsync(
                    $"Write {address}",
                    _ =>
                    {
                        context.Owner = EnsureConnected();
                        return WriteSupportedAsync(context.Owner.Value, parsedAddress, data);
                    },
                    OperationTimeout,
                    () => AbortProtocolAsync(context.Owner),
                    _ => ReleaseProtocolAsync(context.Owner),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"写入地址 {address} 超时，超时时间 {OperationTimeout.TotalSeconds:0} 秒。", ex);
        }
        catch (Exception ex) when (PlcOperationGate.ShouldWrapOperationException(ex))
        {
            throw new InvalidOperationException($"写入地址 {address} 失败。", ex);
        }
    }

    public ValueTask DisposeAsync()
        => _operationGate.DisposeAsync(() => Task.Run(DisconnectCore));

    private bool ConnectCore(
        ProtocolOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (IsConnected)
        {
            return true;
        }

        context.Owner = _protocolOwner;
        DisconnectCore();
        context.Owner = null;

        try
        {
            var protocol = CreateProtocol();
            var owner = new PlcTransportOwner<McpX>(protocol, static value => value.Dispose());
            context.Owner = owner;
            cancellationToken.ThrowIfCancellationRequested();
            _protocolOwner = owner;
            _isConnected = true;
            return true;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            ReleaseProtocol(context.Owner ?? _protocolOwner);
            throw;
        }
    }

    private void DisconnectCore()
    {
        _isConnected = false;
        var owner = _protocolOwner;
        _protocolOwner = null;
        owner?.Release();
    }

    private Task AbortProtocolAsync(PlcTransportOwner<McpX>? owner)
    {
        _isConnected = false;
        return owner is null
            ? Task.CompletedTask
            : Task.Run(owner.Release);
    }

    private Task ReleaseProtocolAsync(PlcTransportOwner<McpX>? owner)
        => owner is null
            ? Task.CompletedTask
            : Task.Run(() => ReleaseProtocol(owner));

    private void ReleaseProtocol(PlcTransportOwner<McpX>? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (ReferenceEquals(_protocolOwner, owner))
        {
            _isConnected = false;
            _protocolOwner = null;
        }

        owner.Release();
    }

    internal static McDeviceAddress ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new FormatException("MC PLC 地址不能为空。");
        }

        var trimmed = address.Trim();
        var splitIndex = 0;
        while (splitIndex < trimmed.Length && char.IsLetter(trimmed[splitIndex]))
        {
            splitIndex++;
        }

        if (splitIndex == 0 || splitIndex == trimmed.Length)
        {
            throw new FormatException($"MC PLC 地址格式无效：{address}");
        }

        var prefixText = trimmed[..splitIndex].ToUpperInvariant();
        var deviceAddress = trimmed[splitIndex..].ToUpperInvariant();
        if (!Enum.TryParse(prefixText, ignoreCase: true, out Prefix prefix))
        {
            throw new FormatException($"MC PLC 地址前缀不支持：{prefixText}");
        }

        var isValidAddress = HexAddressPrefixes.Contains(prefix)
            ? deviceAddress.All(Uri.IsHexDigit)
            : deviceAddress.All(char.IsDigit);
        if (!isValidAddress)
        {
            throw new FormatException($"MC PLC 地址编号无效：{address}");
        }

        return new McDeviceAddress(prefix, deviceAddress);
    }

    private McpX CreateProtocol()
    {
        EnsureInitialized();

        return new McpX(
            _endpoint!.Host,
            _port,
            password: null,
            isAscii: false,
            isUdp: false,
            requestFrame: ResolveRequestFrame(_endpoint.McFrameType),
            timeoutMilliseconds: ResolveTimeoutMilliseconds(_endpoint.ConnectTimeout));
    }

    private void EnsureInitialized()
    {
        if (_endpoint is null || string.IsNullOrWhiteSpace(_endpoint.Host))
        {
            throw new InvalidOperationException("MC PLC 端点未初始化。");
        }
    }

    private PlcTransportOwner<McpX> EnsureConnected()
    {
        if (!IsConnected || _protocolOwner is null)
        {
            throw new InvalidOperationException("PLC 未连接。");
        }

        return _protocolOwner;
    }

    private static ushort ResolveTimeoutMilliseconds(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds;
        if (milliseconds <= 0)
        {
            return 3000;
        }

        return (ushort)Math.Min(milliseconds, ushort.MaxValue);
    }

    private static RequestFrame ResolveRequestFrame(McPlcFrameType frameType)
        => frameType switch
        {
            McPlcFrameType.E3 => RequestFrame.E3,
            McPlcFrameType.E4 => RequestFrame.E4,
            _ => throw new NotSupportedException($"不支持的 MC PLC 协议帧：{frameType}")
        };

    private static Task<List<T>> ReadSupportedAsync<T>(McpX protocol, McDeviceAddress address, ushort length)
        => typeof(T) switch
        {
            var type when type == typeof(bool) => ReadTypedAsync<T, bool>(protocol, address, length),
            var type when type == typeof(short) => ReadTypedAsync<T, short>(protocol, address, length),
            var type when type == typeof(ushort) => ReadTypedAsync<T, ushort>(protocol, address, length),
            var type when type == typeof(int) => ReadTypedAsync<T, int>(protocol, address, length),
            var type when type == typeof(uint) => ReadTypedAsync<T, uint>(protocol, address, length),
            var type when type == typeof(float) => ReadTypedAsync<T, float>(protocol, address, length),
            _ => throw UnsupportedType<T>()
        };

    private static async Task<List<T>> ReadTypedAsync<T, TValue>(McpX protocol, McDeviceAddress address, ushort length)
        where TValue : unmanaged
    {
        var data = await protocol
            .BatchReadAsync<TValue>(address.Prefix, address.Address, length)
            .ConfigureAwait(false);
        return data.Select(static value => (T)(object)value).ToList();
    }

    private static Task WriteSupportedAsync<T>(McpX protocol, McDeviceAddress address, IReadOnlyCollection<T> data)
        => typeof(T) switch
        {
            var type when type == typeof(bool) => WriteTypedAsync<T, bool>(protocol, address, data),
            var type when type == typeof(short) => WriteTypedAsync<T, short>(protocol, address, data),
            var type when type == typeof(ushort) => WriteTypedAsync<T, ushort>(protocol, address, data),
            var type when type == typeof(int) => WriteTypedAsync<T, int>(protocol, address, data),
            var type when type == typeof(uint) => WriteTypedAsync<T, uint>(protocol, address, data),
            var type when type == typeof(float) => WriteTypedAsync<T, float>(protocol, address, data),
            _ => throw UnsupportedType<T>()
        };

    private static Task WriteTypedAsync<T, TValue>(McpX protocol, McDeviceAddress address, IEnumerable<T> data)
        where TValue : unmanaged
        => protocol.BatchWriteAsync(
            address.Prefix,
            address.Address,
            data.Select(static value => (TValue)(object)value!).ToArray());

    private static NotSupportedException UnsupportedType<T>()
        => new($"不支持的数据类型：{typeof(T).Name}");

    private sealed class ProtocolOperationContext
    {
        public PlcTransportOwner<McpX>? Owner { get; set; }
    }
}

internal readonly record struct McDeviceAddress(Prefix Prefix, string Address);
