using IIoT.Edge.Application.Abstractions.Plc;
using McpXLib;
using McpXLib.Enums;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class McPlcService : IPlcService, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
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

    private McpX? _mcProtocol;
    private TcpPlcEndpoint? _endpoint;
    private int _port;
    private bool _isConnected;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _disposed;

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _isConnected && _mcProtocol is not null;

    public void Init(PlcEndpoint endpoint)
    {
        ThrowIfDisposed();
        _endpoint = endpoint as TcpPlcEndpoint
            ?? throw new ArgumentException("MC PLC 只支持 TCP 端点。", nameof(endpoint));
        _port = _endpoint.Port;
    }

    public async Task<bool> ConnectAsync()
    {
        ThrowIfDisposed();
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (IsConnected)
            {
                return true;
            }

            DisconnectCore();
            _mcProtocol = await Task.Run(CreateProtocol).ConfigureAwait(false);
            _isConnected = true;
            return true;
        }
        catch (TimeoutException ex)
        {
            DisconnectCore();
            throw new TimeoutException($"连接 MC PLC {_endpoint!.Host}:{_port} 超时，超时时间 {_endpoint.ConnectTimeout.TotalSeconds:0} 秒。", ex);
        }
        catch
        {
            DisconnectCore();
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Disconnect()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            DisconnectCore();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        ThrowIfDisposed();
        var parsedAddress = ParseAddress(address);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var protocol = EnsureConnected();
            return await ReadSupportedAsync<T>(protocol, parsedAddress, length).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"读取地址 {address} 超时，超时时间 {OperationTimeout.TotalSeconds:0} 秒。", ex);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"读取地址 {address} 失败。", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        ThrowIfDisposed();
        var parsedAddress = ParseAddress(address);

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var protocol = EnsureConnected();
            await WriteSupportedAsync(protocol, parsedAddress, data).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"写入地址 {address} 超时，超时时间 {OperationTimeout.TotalSeconds:0} 秒。", ex);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"写入地址 {address} 失败。", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _semaphore.Wait();
        try
        {
            DisconnectCore();
        }
        finally
        {
            _semaphore.Release();
            _semaphore.Dispose();
        }
    }

    private void DisconnectCore()
    {
        _isConnected = false;
        _mcProtocol?.Dispose();
        _mcProtocol = null;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(McPlcService));
        }
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

    private McpX EnsureConnected()
    {
        if (!IsConnected || _mcProtocol is null)
        {
            throw new InvalidOperationException("PLC 未连接。");
        }

        return _mcProtocol;
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
            .WaitAsync(OperationTimeout)
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
        => protocol
            .BatchWriteAsync(address.Prefix, address.Address, data.Select(static value => (TValue)(object)value!).ToArray())
            .WaitAsync(OperationTimeout);

    private static NotSupportedException UnsupportedType<T>()
        => new($"不支持的数据类型：{typeof(T).Name}");
}

internal readonly record struct McDeviceAddress(Prefix Prefix, string Address);
