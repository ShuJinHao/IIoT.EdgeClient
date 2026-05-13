using IIoT.Edge.Application.Abstractions.Plc;
using PlcClient = S7.Net.Plc;
using S7.Net;
using S7.Net.Types;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class S7PlcService : IPlcService, IDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);

    private PlcClient? _plc;
    private TcpPlcEndpoint? _endpoint;
    private int _port;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsConnected => _plc?.IsConnected ?? false;

    public void Init(PlcEndpoint endpoint)
    {
        _endpoint = endpoint as TcpPlcEndpoint
            ?? throw new ArgumentException("S7 PLC 只支持 TCP 端点。", nameof(endpoint));
        _port = _endpoint.Port;
    }

    public async Task<bool> ConnectAsync()
    {
        EnsureInitialized();

        if (_plc?.IsConnected == true)
        {
            return true;
        }

        ReleasePlc(_plc);
        _plc = new PlcClient(CpuType.S71200, _endpoint!.Host, 0, 1);

        using var timeoutCts = new CancellationTokenSource(_endpoint.ConnectTimeout);

        try
        {
            await _plc.OpenAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!_plc.IsConnected)
            {
                throw new InvalidOperationException($"S7 PLC {_endpoint.Host}:{_port} reported success but is still disconnected.");
            }

            return true;
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested)
        {
            ReleasePlc(_plc);
            _plc = null;
            throw new TimeoutException($"Connect to S7 PLC {_endpoint.Host}:{_port} timed out after {_endpoint.ConnectTimeout.TotalSeconds:0}s.", ex);
        }
        catch
        {
            ReleasePlc(_plc);
            _plc = null;
            throw;
        }
    }

    public void Disconnect() => _plc?.Close();

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        var plc = EnsureConnected();

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeoutCts = new CancellationTokenSource(OperationTimeout);

            if (typeof(T) == typeof(ushort)
                && TryParseDbWordAddress(address, out var dbNumber, out var startByteAddress))
            {
                var rawBytes = await plc
                    .ReadBytesAsync(
                        DataType.DataBlock,
                        dbNumber,
                        startByteAddress,
                        checked(length * 2),
                        timeoutCts.Token)
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
                var value = await plc.ReadAsync(currentAddress, timeoutCts.Token).ConfigureAwait(false);
                if (value is null)
                {
                    throw new InvalidOperationException($"Read {currentAddress} returned null.");
                }

                result.Add(ConvertValue<T>(value));
            }

            return result;
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException($"Read {address} timed out after {OperationTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"Read {address} failed.", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        var plc = EnsureConnected();

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeoutCts = new CancellationTokenSource(OperationTimeout);

            if (typeof(T) == typeof(ushort)
                && TryParseDbWordAddress(address, out var dbNumber, out var startByteAddress))
            {
                var words = data.Cast<ushort>().ToArray();
                var bytes = Word.ToByteArray(words);
                await plc
                    .WriteBytesAsync(DataType.DataBlock, dbNumber, startByteAddress, bytes, timeoutCts.Token)
                    .ConfigureAwait(false);
                return;
            }

            for (var i = 0; i < data.Count; i++)
            {
                var currentAddress = GetIndexedAddress(address, i);
                await plc.WriteAsync(currentAddress, data[i]!, timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException($"Write {address} timed out after {OperationTimeout.TotalSeconds:0}s.", ex);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"Write {address} failed.", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        ReleasePlc(_plc);
        _plc = null;
        _semaphore.Dispose();
    }

    private static void ReleasePlc(PlcClient? plc)
    {
        if (plc is null)
        {
            return;
        }

        plc.Close();
        ((IDisposable)plc).Dispose();
    }

    private void EnsureInitialized()
    {
        if (_endpoint is null || string.IsNullOrWhiteSpace(_endpoint.Host))
        {
            throw new InvalidOperationException("S7 PLC endpoint is not initialized.");
        }
    }

    private PlcClient EnsureConnected()
    {
        if (_plc is null || !_plc.IsConnected)
        {
            throw new InvalidOperationException("PLC is not connected.");
        }

        return _plc;
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
}
