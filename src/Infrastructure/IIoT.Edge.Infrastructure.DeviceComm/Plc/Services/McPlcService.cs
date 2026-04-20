using IIoT.Edge.Application.Abstractions.Plc;
using MCProtocol;
using static MCProtocol.Mitsubishi;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class McPlcService : IPlcService, IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);

    private McProtocolTcp? _mcProtocol;
    private string _ip = string.Empty;
    private int _port;
    private readonly McFrame _frameType = McFrame.MC3E;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsConnected => _mcProtocol?.Connected ?? false;

    public void Init(string ip, int port)
    {
        _ip = ip;
        _port = port;
    }

    public async Task<bool> ConnectAsync()
    {
        EnsureInitialized();

        if (_mcProtocol?.Connected == true)
        {
            return true;
        }

        _mcProtocol?.Close();
        _mcProtocol = new McProtocolTcp(_ip, _port, _frameType);

        try
        {
            await _mcProtocol.Open().WaitAsync(ConnectTimeout).ConfigureAwait(false);
            if (!_mcProtocol.Connected)
            {
                throw new InvalidOperationException($"MC PLC {_ip}:{_port} reported success but is still disconnected.");
            }

            return true;
        }
        catch (TimeoutException ex)
        {
            _mcProtocol.Close();
            throw new TimeoutException($"Connect to MC PLC {_ip}:{_port} timed out after {ConnectTimeout.TotalSeconds:0}s.", ex);
        }
        catch
        {
            _mcProtocol.Close();
            throw;
        }
    }

    public void Disconnect() => _mcProtocol?.Close();

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        var protocol = EnsureConnected();

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (typeof(T) == typeof(bool))
            {
                var data = new int[length];
                await protocol.GetBitDevice(address, length, data).WaitAsync(OperationTimeout).ConfigureAwait(false);
                return data.Select(x => (T)(object)(x != 0)).ToList();
            }

            var wordSize = GetWordSize(typeof(T));
            var totalWords = length * wordSize;
            var dataBuffer = new int[totalWords];
            await protocol.ReadDeviceBlock(address, totalWords, dataBuffer).WaitAsync(OperationTimeout).ConfigureAwait(false);
            return ConvertToTypeList<T>(dataBuffer, length);
        }
        catch (TimeoutException ex)
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
        var protocol = EnsureConnected();

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (typeof(T) == typeof(bool))
            {
                var intData = data.Select(x => Convert.ToInt32(x)).ToArray();
                await protocol.SetBitDevice(address, data.Count, intData).WaitAsync(OperationTimeout).ConfigureAwait(false);
                return;
            }

            var wordSize = GetWordSize(typeof(T));
            var intDataArray = ConvertToIntArray(data, wordSize);
            await protocol.WriteDeviceBlock(address, intDataArray.Length, intDataArray).WaitAsync(OperationTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
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
        Disconnect();
        _mcProtocol?.Dispose();
        _mcProtocol = null;
    }

    private void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(_ip))
        {
            throw new InvalidOperationException("MC PLC endpoint is not initialized.");
        }
    }

    private McProtocolTcp EnsureConnected()
    {
        if (_mcProtocol is null || !_mcProtocol.Connected)
        {
            throw new InvalidOperationException("PLC is not connected.");
        }

        return _mcProtocol;
    }

    private static int GetWordSize(Type type)
    {
        if (type == typeof(bool)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 1;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 2;
        throw new NotSupportedException($"Unsupported type: {type.Name}");
    }

    private static List<T> ConvertToTypeList<T>(int[] source, int elementCount)
    {
        var result = new List<T>();
        var wordSize = GetWordSize(typeof(T));

        for (var i = 0; i < elementCount; i++)
        {
            var idx = i * wordSize;
            object value = Type.GetTypeCode(typeof(T)) switch
            {
                TypeCode.Int16 => (short)(ushort)source[idx],
                TypeCode.UInt16 => (ushort)source[idx],
                TypeCode.Int32 => CombineToInt32(source[idx + 1], source[idx]),
                TypeCode.UInt32 => CombineToUInt32(source[idx + 1], source[idx]),
                TypeCode.Single => CombineToFloat(source[idx + 1], source[idx]),
                _ => throw new NotSupportedException($"Unsupported type: {typeof(T).Name}")
            };
            result.Add((T)value);
        }

        return result;
    }

    private static int[] ConvertToIntArray<T>(List<T> data, int wordSize)
    {
        var result = new int[data.Count * wordSize];
        for (var i = 0; i < data.Count; i++)
        {
            var idx = i * wordSize;
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    result[idx] = Convert.ToInt32(data[i]);
                    break;
                case TypeCode.Int32:
                    SplitInt32(Convert.ToInt32(data[i]), out result[idx + 1], out result[idx]);
                    break;
                case TypeCode.UInt32:
                    SplitUInt32(Convert.ToUInt32(data[i]), out result[idx + 1], out result[idx]);
                    break;
                case TypeCode.Single:
                    SplitFloat(Convert.ToSingle(data[i]), out result[idx + 1], out result[idx]);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported type: {typeof(T).Name}");
            }
        }

        return result;
    }

    private static int CombineToInt32(int high, int low)
        => ((ushort)high << 16) | (ushort)low;

    private static uint CombineToUInt32(int high, int low)
        => ((uint)(ushort)high << 16) | (ushort)low;

    private static float CombineToFloat(int high, int low)
    {
        byte[] bytes =
        [
            (byte)((ushort)high >> 8),
            (byte)(ushort)high,
            (byte)((ushort)low >> 8),
            (byte)(ushort)low
        ];

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }

    private static void SplitInt32(int value, out int high, out int low)
    {
        high = (value >> 16) & 0xFFFF;
        low = value & 0xFFFF;
    }

    private static void SplitUInt32(uint value, out int high, out int low)
    {
        high = (int)((value >> 16) & 0xFFFF);
        low = (int)(value & 0xFFFF);
    }

    private static void SplitFloat(float value, out int high, out int low)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        high = (bytes[0] << 8) | bytes[1];
        low = (bytes[2] << 8) | bytes[3];
    }
}
