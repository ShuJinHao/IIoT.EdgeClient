using IIoT.Edge.Module.Contracts.Plc;
using PlcClient = S7.Net.Plc;
using S7.Net;
using S7.Net.Types;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class S7PlcService : IPlcService
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OperationSettleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly PlcOperationGate _operationGate = new(
        nameof(S7PlcService),
        OperationSettleTimeout,
        DisposeTimeout);
    private PlcTransportOwner<PlcClient>? _plcOwner;
    private TcpPlcEndpoint? _endpoint;
    private int _port;

    public bool IsConnected
        => _operationGate.IsOpen
           && _plcOwner?.ValueOrDefault?.IsConnected == true;

    public void Init(PlcEndpoint endpoint)
    {
        _operationGate.ThrowIfNotOpen(nameof(Init));
        _endpoint = endpoint as TcpPlcEndpoint
            ?? throw new ArgumentException("S7 PLC 只支持 TCP 端点。", nameof(endpoint));
        _port = _endpoint.Port;
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new PlcOperationContext();
        return _operationGate.ExecuteAsync(
            nameof(ConnectAsync),
            token => Task.Run(() => ConnectCoreAsync(context, token), CancellationToken.None),
            GetConnectTimeout(),
            () => AbortPlcAsync(context.Owner ?? _plcOwner),
            _ => ReleasePlcAsync(context.Owner ?? _plcOwner),
            cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new PlcOperationContext();
        return _operationGate.ExecuteAsync(
            nameof(DisconnectAsync),
            token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                context.Owner = _plcOwner;
                DisconnectCore();
            }, CancellationToken.None),
            OperationTimeout,
            () => AbortPlcAsync(context.Owner ?? _plcOwner),
            _ => ReleasePlcAsync(context.Owner ?? _plcOwner),
            cancellationToken);
    }

    public async Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default)
        => await ExecutePlcOperationAsync(
            "Read",
            address,
            async (plc, cancellationToken) =>
            {
                if (typeof(T) == typeof(ushort)
                    && TryParseDbWordAddress(address, out var dbNumber, out var startByteAddress))
                {
                    var rawBytes = await plc
                        .ReadBytesAsync(
                            DataType.DataBlock,
                            dbNumber,
                            startByteAddress,
                            checked(length * 2),
                            cancellationToken)
                        .ConfigureAwait(false);

                    var words = Word.ToArray(rawBytes);
                    if (words.Length < length)
                    {
                        throw new InvalidOperationException(
                            $"Read {address} returned {words.Length} word(s), expected {length}.");
                    }

                    return words
                        .Take(length)
                        .Select(value => (T)(object)value)
                        .ToList();
                }

                var result = new List<T>(length);
                for (var i = 0; i < length; i++)
                {
                    var currentAddress = GetIndexedAddress(address, i);
                    var value = await plc.ReadAsync(currentAddress, cancellationToken).ConfigureAwait(false);
                    if (value is null)
                    {
                        throw new InvalidOperationException($"Read {currentAddress} returned null.");
                    }

                    result.Add(ConvertValue<T>(value));
                }

                return result;
            },
            cancellationToken)
            .ConfigureAwait(false);

    public async Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default)
        => await ExecutePlcOperationAsync<object?>(
            "Write",
            address,
            async (plc, cancellationToken) =>
            {
                if (typeof(T) == typeof(ushort)
                    && TryParseDbWordAddress(address, out var dbNumber, out var startByteAddress))
                {
                    var words = data.Cast<ushort>().ToArray();
                    var bytes = Word.ToByteArray(words);
                    await plc
                        .WriteBytesAsync(DataType.DataBlock, dbNumber, startByteAddress, bytes, cancellationToken)
                        .ConfigureAwait(false);
                    return null;
                }

                for (var i = 0; i < data.Count; i++)
                {
                    var currentAddress = GetIndexedAddress(address, i);
                    await plc.WriteAsync(currentAddress, data[i]!, cancellationToken).ConfigureAwait(false);
                }

                return null;
            },
            cancellationToken)
            .ConfigureAwait(false);

    private async Task<TResult> ExecutePlcOperationAsync<TResult>(
        string operation,
        string address,
        Func<PlcClient, CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var context = new PlcOperationContext();
        try
        {
            return await _operationGate.ExecuteAsync(
                    $"{operation} {address}",
                    token =>
                    {
                        context.Owner = EnsureConnected();
                        return action(context.Owner.Value, token);
                    },
                    OperationTimeout,
                    () => AbortPlcAsync(context.Owner),
                    _ => ReleasePlcAsync(context.Owner),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (PlcOperationGate.ShouldWrapOperationException(ex))
        {
            throw new InvalidOperationException($"{operation} {address} failed.", ex);
        }
    }

    public ValueTask DisposeAsync()
        => _operationGate.DisposeAsync(() => Task.Run(DisconnectCore));

    private async Task<bool> ConnectCoreAsync(
        PlcOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (IsConnected)
        {
            return true;
        }

        context.Owner = _plcOwner;
        DisconnectCore();
        context.Owner = null;

        var endpoint = _endpoint!;
        var client = new PlcClient(CpuType.S71200, endpoint.Host, 0, 1);
        var owner = new PlcTransportOwner<PlcClient>(client, ReleasePlcClient);
        context.Owner = owner;
        try
        {
            await client.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (!client.IsConnected)
            {
                throw new InvalidOperationException(
                    $"S7 PLC {endpoint.Host}:{_port} reported success but is still disconnected.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _plcOwner = owner;
            return true;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            ReleasePlc(owner);
            throw;
        }
    }

    private void DisconnectCore()
    {
        var owner = _plcOwner;
        _plcOwner = null;
        owner?.Release();
    }

    private TimeSpan GetConnectTimeout()
        => _endpoint?.ConnectTimeout > TimeSpan.Zero
            ? _endpoint.ConnectTimeout
            : OperationTimeout;

    private static Task AbortPlcAsync(PlcTransportOwner<PlcClient>? owner)
        => owner is null
            ? Task.CompletedTask
            : Task.Run(owner.Release);

    private Task ReleasePlcAsync(PlcTransportOwner<PlcClient>? owner)
        => owner is null
            ? Task.CompletedTask
            : Task.Run(() => ReleasePlc(owner));

    private void ReleasePlc(PlcTransportOwner<PlcClient>? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (ReferenceEquals(_plcOwner, owner))
        {
            _plcOwner = null;
        }

        owner.Release();
    }

    private static void ReleasePlcClient(PlcClient plc)
    {
        try
        {
            plc.Close();
        }
        finally
        {
            ((IDisposable)plc).Dispose();
        }
    }

    private void EnsureInitialized()
    {
        if (_endpoint is null || string.IsNullOrWhiteSpace(_endpoint.Host))
        {
            throw new InvalidOperationException("S7 PLC endpoint is not initialized.");
        }
    }

    private PlcTransportOwner<PlcClient> EnsureConnected()
    {
        if (!IsConnected || _plcOwner is null)
        {
            throw new InvalidOperationException("PLC is not connected.");
        }

        return _plcOwner;
    }

    private static string GetIndexedAddress(string baseAddress, int index)
        => baseAddress.Contains('[') ? baseAddress : $"{baseAddress}[{index}]";

    private static bool TryParseDbWordAddress(string address, out int dbNumber, out int startByteAddress)
    {
        dbNumber = 0;
        startByteAddress = 0;

        if (string.IsNullOrWhiteSpace(address) || address.Contains('['))
        {
            return false;
        }

        var separatorIndex = address.IndexOf(".DBW", StringComparison.OrdinalIgnoreCase);
        if (separatorIndex <= 2 || !address.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(address[2..separatorIndex], out dbNumber)
            && int.TryParse(address[(separatorIndex + 4)..], out startByteAddress);
    }

    private static T ConvertValue<T>(object value)
    {
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Convert {value.GetType().Name} to {typeof(T).Name} failed.",
                ex);
        }
    }

    private sealed class PlcOperationContext
    {
        public PlcTransportOwner<PlcClient>? Owner { get; set; }
    }
}
